using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Text.Json;

namespace Automaton.Worker.Tests;

public class HostedRunnerStorageReconciliationServiceTests
{
    [Test]
    public async Task RecentHealthyReport_DoesNotRequestVerification()
    {
        await using var fixture = await HostedRunnerStorageReconciliationFixture.CreateAsync(HostedRunnerStorageStates.Healthy, DateTime.UtcNow);
        await fixture.Service.ReconcileAsync(default);
        Assert.That(fixture.Db.HostedRunnerStorageVerifications, Is.Empty);
    }

    [Test]
    public async Task RecentHealthySelfReportWithoutPriorVerification_DoesNotRequestVerification()
    {
        await using var fixture = await HostedRunnerStorageReconciliationFixture.CreateAsync(HostedRunnerStorageStates.Healthy, DateTime.UtcNow);
        var runner = fixture.Db.HostedRunners.Single();
        runner.StorageVerifiedAtUtc = null;
        runner.StorageSelfReportedStatus = StorageMeasurementStatuses.Succeeded;
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.ReconcileAsync(default);
        Assert.That(fixture.Db.HostedRunnerStorageVerifications, Is.Empty);
    }

    [Test]
    public async Task ExpiredHealthyVerification_IsSampledWhenAuditModeDoesNotSelectRunner()
    {
        await using var fixture = await HostedRunnerStorageReconciliationFixture.CreateAsync(HostedRunnerStorageStates.Healthy, DateTime.UtcNow,
            new StorageTelemetryOptions { EnableLowFrequencyVerification = true, LowFrequencyVerificationSamplePercent = 0 });
        fixture.Db.HostedRunners.Single().StorageVerifiedAtUtc = DateTime.UtcNow.AddDays(-2);
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.ReconcileAsync(default);
        Assert.That(fixture.Db.HostedRunnerStorageVerifications, Is.Empty);
    }

    [Test]
    public async Task StaleReport_RequestsExactlyOneVerificationAcrossRepeatedReconciliation()
    {
        await using var fixture = await HostedRunnerStorageReconciliationFixture.CreateAsync(HostedRunnerStorageStates.Healthy, DateTime.UtcNow.AddHours(-3));
        await fixture.Service.ReconcileAsync(default);
        await fixture.Service.ReconcileAsync(default);
        Assert.That(fixture.Db.HostedRunnerStorageVerifications.Count(), Is.EqualTo(1));
        Assert.That(fixture.Db.HostedRunnerStorageVerifications.Single().Reason, Is.EqualTo("Stale"));
    }

    [Test]
    public async Task WarningReport_RequestsVerification()
    {
        await using var fixture = await HostedRunnerStorageReconciliationFixture.CreateAsync(HostedRunnerStorageStates.Warning, DateTime.UtcNow);
        await fixture.Service.ReconcileAsync(default);
        Assert.That(fixture.Db.HostedRunnerStorageVerifications.Single().Reason, Is.EqualTo("Threshold"));
    }

    [Test]
    public async Task DispatchBound_ExcludesAlreadyRunningVerificationsPerHost()
    {
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync(maxPerHost: 2);
        await fixture.SeedRunnerAsync();
        await fixture.AddVerificationAsync(StorageVerificationStatuses.Dispatching, "active");
        await fixture.AddVerificationAsync(StorageVerificationStatuses.Running, "active");
        var pendingId = await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "pending");
        await fixture.ReconcileAsync();
        Assert.That(await fixture.VerificationStatusAsync(pendingId), Is.EqualTo(StorageVerificationStatuses.Pending));
    }

    [Test]
    public async Task TimedOutDispatch_WithTerminalDeployment_IsFailedInsteadOfRedispatched()
    {
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync(maxPerHost: 2);
        await fixture.SeedRunnerAsync();
        var verificationId = await fixture.AddVerificationAsync(StorageVerificationStatuses.Dispatching, "AdministratorRequested",
            startedAtUtc: DateTime.UtcNow.AddHours(-1));
        await fixture.AddTerminalVerificationDeploymentAsync(verificationId, HostedRunnerDeploymentStatuses.Failed, "Old action failed.");
        var pendingId = await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "AdministratorRequested");

        await fixture.ReconcileAsync();
        var failedStatus = await fixture.VerificationStatusAsync(verificationId);
        var failedError = await fixture.VerificationErrorAsync(verificationId);
        var pendingStatus = await fixture.VerificationStatusAsync(pendingId);

        Assert.Multiple(() =>
        {
            Assert.That(failedStatus, Is.EqualTo(StorageVerificationStatuses.Failed));
            Assert.That(failedError, Is.EqualTo("Old action failed."));
            Assert.That(pendingStatus, Is.EqualTo(StorageVerificationStatuses.Dispatching));
            Assert.That(fixture.CommandCount, Is.EqualTo(1));
            Assert.That(fixture.SingleCommand().CommandId, Is.EqualTo(pendingId));
        });
    }

    [Test]
    public async Task TimedOutDispatch_WithoutTerminalDeployment_IsRecoveredAndRepublished()
    {
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync();
        await fixture.SeedRunnerAsync();
        var verificationId = await fixture.AddVerificationAsync(StorageVerificationStatuses.Dispatching, "AdministratorRequested",
            startedAtUtc: DateTime.UtcNow.AddHours(-1));

        await fixture.ReconcileAsync();
        var status = await fixture.VerificationStatusAsync(verificationId);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(StorageVerificationStatuses.Dispatching));
            Assert.That(fixture.CommandCount, Is.EqualTo(1));
            Assert.That(fixture.SingleCommand().CommandId, Is.EqualTo(verificationId));
        });
    }

    [Test]
    public async Task StalePendingVerification_WithTerminalDeployment_IsClosedWithoutRedispatch()
    {
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync();
        await fixture.SeedRunnerAsync();
        var verificationId = await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "AdministratorRequested",
            requestedAt: DateTime.UtcNow.AddDays(-1));
        await fixture.AddTerminalVerificationDeploymentAsync(verificationId, HostedRunnerDeploymentStatuses.Failed, "Previous command already failed.");

        await fixture.ReconcileAsync();

        var status = await fixture.VerificationStatusAsync(verificationId);
        var error = await fixture.VerificationErrorAsync(verificationId);
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(StorageVerificationStatuses.Failed));
            Assert.That(error, Is.EqualTo("Previous command already failed."));
            Assert.That(fixture.CommandCount, Is.Zero);
        });
    }

    [Test]
    public async Task DispatchBound_IgnoresStalePendingDeploymentMirrors()
    {
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync(maxPerHost: 2);
        await fixture.SeedRunnerAsync(storageProjectId: 1_234_568);
        await fixture.AddDeploymentMirrorsAsync(2);
        var verificationId = await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "AdministratorRequested");
        await fixture.ReconcileAsync();
        var command = fixture.SingleCommand();
        var status = await fixture.VerificationStatusAsync(verificationId);
        var deploymentCount = await fixture.VerificationDeploymentCountAsync();
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(StorageVerificationStatuses.Dispatching));
            Assert.That(deploymentCount, Is.EqualTo(3));
            Assert.That(command.CommandId, Is.EqualTo(verificationId));
            Assert.That(command.OperationId, Is.EqualTo(verificationId));
            Assert.That(command.ActionKey, Is.EqualTo(StorageTelemetryActions.MeasureHostedRunnerStorage));
            Assert.That(command.RunnerHostId, Is.EqualTo(fixture.HostId));
            Assert.That(command.InfrastructureRunnerId, Is.EqualTo(fixture.InfrastructureRunnerId));
        });
        using var input = JsonDocument.Parse(command.InputJson);
        Assert.That(input.RootElement.GetProperty("storageAllowanceBytes").GetInt64(), Is.EqualTo(512L * 1024 * 1024));
    }

    [Test]
    public async Task AdministratorRequestedVerification_OutranksRoutineBacklog()
    {
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync();
        await fixture.SeedRunnerAsync();
        await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "Stale", DateTime.UtcNow.AddDays(-2));
        await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "NeverMeasured", DateTime.UtcNow.AddDays(-1));
        var administratorVerificationId = await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "AdministratorRequested");
        await fixture.ReconcileAsync();
        var pendingCount = await fixture.PendingVerificationCountAsync();
        var status = await fixture.VerificationStatusAsync(administratorVerificationId);
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(StorageVerificationStatuses.Dispatching));
            Assert.That(pendingCount, Is.EqualTo(2));
            Assert.That(fixture.SingleCommand().CommandId, Is.EqualTo(administratorVerificationId));
        });
    }

    [Test]
    public async Task EqualPriorityAdministratorRequestsRemainFifo()
    {
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync();
        await fixture.SeedRunnerAsync(storageProjectId: 1_234_569);
        var requestedAt = DateTime.UtcNow.AddMinutes(-2);
        var olderId = await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "AdministratorRequested", requestedAt);
        var newerId = await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "AdministratorRequested", requestedAt.AddMinutes(1));
        await fixture.ReconcileAsync();
        var olderStatus = await fixture.VerificationStatusAsync(olderId);
        var newerStatus = await fixture.VerificationStatusAsync(newerId);
        Assert.Multiple(() =>
        {
            Assert.That(olderStatus, Is.EqualTo(StorageVerificationStatuses.Dispatching));
            Assert.That(newerStatus, Is.EqualTo(StorageVerificationStatuses.Pending));
            Assert.That(fixture.SingleCommand().CommandId, Is.EqualTo(olderId));
        });
    }

    [Test]
    public async Task ProvisioningRunner_DefersPendingVerificationUntilContainerExists()
    {
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync();
        await fixture.SeedRunnerAsync(provisioning: true, storageProjectId: 1_234_571);
        var verificationId = await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "AdministratorRequested");
        await fixture.ReconcileAsync();
        var status = await fixture.VerificationStatusAsync(verificationId);
        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(StorageVerificationStatuses.Pending));
            Assert.That(fixture.CommandCount, Is.Zero);
            Assert.That(fixture.VerificationDeploymentCountAsync().Result, Is.Zero);
        });
    }

    [Test]
    public async Task FailedVerification_IsNotRepublished()
    {
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync();
        await fixture.SeedRunnerAsync(storageProjectId: 1_234_572);
        await fixture.AddVerificationAsync(StorageVerificationStatuses.Failed, "AdministratorRequested");
        await fixture.ReconcileAsync();
        Assert.That(fixture.CommandCount, Is.Zero);
    }

    [Test]
    public async Task PendingVerification_PublishesDurableMeasurementCommand()
    {
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync();
        await fixture.SeedRunnerAsync(storageProjectId: 1_234_570);
        var verificationId = await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "Stale");
        await fixture.ReconcileAsync();
        var command = fixture.SingleCommand();
        Assert.Multiple(() =>
        {
            Assert.That(command.CommandId, Is.EqualTo(verificationId));
            Assert.That(command.OperationId, Is.EqualTo(verificationId));
            Assert.That(command.ActionKey, Is.EqualTo(StorageTelemetryActions.MeasureHostedRunnerStorage));
            Assert.That(command.RunnerHostId, Is.EqualTo(fixture.HostId));
            Assert.That(command.InfrastructureRunnerId, Is.EqualTo(fixture.InfrastructureRunnerId));
        });
        using var input = JsonDocument.Parse(command.InputJson);
        Assert.That(input.RootElement.TryGetProperty("storageFilesystemUuid", out _), Is.False);
    }

    [Test]
    public async Task PendingVerification_IncludesKnownFilesystemUuid()
    {
        const string filesystemUuid = "e32f7db1-0a12-4b1a-9876-d7c3e8140dc1";
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync();
        await fixture.SeedRunnerAsync(storageFilesystemUuid: filesystemUuid);
        await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "AdministratorRequested");

        await fixture.ReconcileAsync();

        using var input = JsonDocument.Parse(fixture.SingleCommand().InputJson);
        Assert.That(input.RootElement.GetProperty("storageFilesystemUuid").GetString(), Is.EqualTo(filesystemUuid));
    }

    [Test]
    public async Task PendingVerification_RejectsConflictingStorageHistoryWithoutAdoptingHost()
    {
        await using var fixture = await HostedRunnerStorageDispatchHarness.CreateAsync();
        await fixture.SeedRunnerAsync();
        var verificationId = await fixture.AddVerificationAsync(StorageVerificationStatuses.Pending, "AdministratorRequested");
        await fixture.AddConflictingStorageHostAsync();

        await fixture.ReconcileAsync();
        var storageHostId = await fixture.StorageRunnerHostIdAsync();
        var verificationStatus = await fixture.VerificationStatusAsync(verificationId);
        var verificationError = await fixture.VerificationErrorAsync(verificationId);

        Assert.Multiple(() =>
        {
            Assert.That(fixture.CommandCount, Is.Zero);
            Assert.That(storageHostId, Is.Null);
            Assert.That(verificationStatus, Is.EqualTo(StorageVerificationStatuses.Failed));
            Assert.That(verificationError, Does.Contain("multiple Runner Hosts"));
        });
    }

}
