using System.Text.Json;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Messaging.Events;

namespace Automaton.Api.Tests;

[TestFixture]
public class HostedRunnerAdminLifecycleAuditTests
{

    [Test]
    public async Task RestartAsAdminAsync_WritesRequestedAuditAndQueuesAction()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var hostedRunner = HostedRunnerAuditTestData.SeedManagedHostedRunner(db, userId);

        var managementService = HostedRunnerManagementTestServiceFactory.Create(
            db,
            userId);

        await HostedRunnerAdminAuditAssertions.AssertRestartRequestQueuedAsync(db, managementService, hostedRunner.Id);
    }


    [Test]
    public async Task RestartAsAdminAsync_QueuesRestartWithoutVolumeDisposition()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var hostedRunner = HostedRunnerAuditTestData.SeedManagedHostedRunner(db, userId);
        var publisher = new HostedRunnerRecordingEventPublisher();
        var managementService = HostedRunnerManagementTestServiceFactory.Create(
            db,
            userId,
            publisher);

        await managementService.RestartAsAdminAsync(hostedRunner.Id, CancellationToken.None);

        var request = publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single();
        Assert.That(request.ActionKey, Is.EqualTo("restart-hosted-runner"));
        Assert.That(request.CommandId, Is.EqualTo(request.OperationId));
        Assert.That(request.RunnerHostId, Is.EqualTo(hostedRunner.RunnerHostId));
        Assert.That(request.InfrastructureRunnerId, Is.EqualTo(hostedRunner.RunnerHost!.InfrastructureRunnerId));
        Assert.That(request.InputJson, Does.Not.Contain("preserveVolumes"));
        Assert.That(request.InputJson, Does.Not.Contain("VolumeName"));
    }


    [TestCase(HostedRunnerDataDispositions.Preserve, true)]
    [TestCase(HostedRunnerDataDispositions.Delete, false)]
    public async Task DeleteAsAdminAsync_PublishesExplicitDataDisposition(
        string disposition,
        bool expectedPreserveVolumes)
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var hostedRunner = HostedRunnerAuditTestData.SeedManagedHostedRunner(db, userId);
        hostedRunner.RuntimePort = 20000;
        await db.SaveChangesAsync();
        var publisher = new HostedRunnerRecordingEventPublisher();
        var managementService = HostedRunnerManagementTestServiceFactory.Create(
            db,
            userId,
            publisher);

        await managementService.DeleteAsAdminAsync(
            hostedRunner.Id,
            disposition,
            CancellationToken.None);

        var request = publisher.Messages.OfType<InfrastructureCommandRequestedEvent>().Single();
        using var input = JsonDocument.Parse(request.InputJson);
        Assert.Multiple(() =>
        {
            Assert.That(request.ActionKey, Is.EqualTo("delete-hosted-runner"));
            Assert.That(input.RootElement.GetProperty("preserveVolumes").GetBoolean(),
                Is.EqualTo(expectedPreserveVolumes));
            Assert.That(input.RootElement.GetProperty("systemVolumeName").GetString(),
                Is.EqualTo($"automaton-hosted-runner-{hostedRunner.Id:N}-system"));
            Assert.That(input.RootElement.GetProperty("userVolumeName").GetString(),
                Is.EqualTo($"automaton-hosted-runner-{hostedRunner.Id:N}-user"));
            Assert.That(hostedRunner.RuntimePort, Is.EqualTo(20000));
        });
    }


    [TestCase(null)]
    [TestCase("")]
    [TestCase("invalid")]
    public void DeleteAsAdminAsync_RejectsMissingOrInvalidDataDisposition(string? disposition)
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var managementService = HostedRunnerManagementTestServiceFactory.Create(
            db,
            userId);

        var exception = Assert.ThrowsAsync<ArgumentException>(() =>
            managementService.DeleteAsAdminAsync(
                Guid.NewGuid(),
                disposition!,
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("preserve").And.Contain("delete"));
    }


    [Test]
    public async Task RestartAsAdminAsync_DoesNotExecuteDockerSynchronously()
    {
        using var db = HostedRunnerAuditTestData.CreateDbContext();
        var userId = Guid.NewGuid();
        var hostedRunner = HostedRunnerAuditTestData.SeedManagedHostedRunner(db, userId);

        var managementService = HostedRunnerManagementTestServiceFactory.Create(
            db,
            userId);

        await managementService.RestartAsAdminAsync(hostedRunner.Id, CancellationToken.None);

        Assert.That(db.AuditEvents.Any(x => x.EventType == AuditEventTypes.RunnerRestartFailed), Is.False);
        Assert.That(db.HostedRunnerDeployments.Single().Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Queued));
    }
}
