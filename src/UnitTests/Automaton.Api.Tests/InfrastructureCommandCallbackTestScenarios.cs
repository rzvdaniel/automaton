using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

internal static class InfrastructureCommandCallbackTestScenarios
{
    public static async Task<CallbackFixture> CreateAsync(
        string hostedRunnerStatus = HostedRunnerStatuses.Provisioning,
        string deploymentAction = HostedRunnerDeploymentActions.Restart,
        string deploymentStatus = HostedRunnerDeploymentStatuses.Queued,
        string? storageState = null,
        bool addStorageVerification = false,
        long storageAllowanceBytes = 0,
        bool completeStorageLifecycle = false,
        int? runtimePort = null)
    {
        var db = CreateDb();
        var apiKeyId = Guid.NewGuid();
        var runner = CreateInfrastructureRunner(apiKeyId);
        var host = CreateRunnerHost(runner);
        var hosted = CreateHostedRunner(host, hostedRunnerStatus, storageState, storageAllowanceBytes, runtimePort);
        var deployment = CreateDeployment(hosted, deploymentAction, deploymentStatus);
        db.AddRange(runner, host, hosted, deployment);
        db.AccountStorageAllocations.Add(new AccountStorageAllocation
        {
            AccountId = hosted.UserId,
            StorageProjectId = hosted.StorageProjectId!.Value,
            StorageRunnerHostId = host.Id
        });
        if (addStorageVerification)
            db.HostedRunnerStorageVerifications.Add(CreateStorageVerification(deployment.Id, hosted.Id));

        await db.SaveChangesAsync();
        var lifecycle = completeStorageLifecycle ? CreateStorageProvisioningHandler(db) : null;
        return new CallbackFixture(db, apiKeyId, runner, host, hosted, deployment,
            CreateService(db, lifecycle));
    }

    public static InfrastructureCommandResultRequest CreateSuccessfulStorageMeasurementResult(CallbackFixture fixture)
        => new(
            fixture.Deployment.Id,
            fixture.Host.Id,
            fixture.Runner.Id,
            StorageTelemetryActions.MeasureHostedRunnerStorage,
            [],
            new AppActionExecutionResult
            {
                ExecutionId = fixture.Deployment.Id,
                Status = AppModuleExecutionStatuses.Succeeded,
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow
            },
            new AppModuleExecutionArtifactResponse(true,
                "{\"success\":true,\"systemBytes\":0,\"userBytes\":100,\"totalBytes\":100,\"usageSource\":\"xfs-project-quota\",\"storageFilesystemUuid\":\"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa\"}",
                null,
                null));

    public static async Task AssertFailedStorageMeasurementAsync(
        CallbackFixture fixture,
        InfrastructureCommandResultResponse result)
    {
        var verification = await fixture.Db.HostedRunnerStorageVerifications.SingleAsync();
        var persistedRunner = await fixture.Db.HostedRunners.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(InfrastructureCommandResultOutcomes.Accepted));
            Assert.That(verification.Status, Is.EqualTo(StorageVerificationStatuses.Failed));
            Assert.That(verification.CompletedAtUtc, Is.Not.Null);
            Assert.That(verification.Error, Is.EqualTo("expected volume is not provisioned"));
            Assert.That(persistedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Running));
            Assert.That(persistedRunner.StorageMeasurementStatus, Is.EqualTo(StorageMeasurementStatuses.Failed));
            Assert.That(persistedRunner.StorageState, Is.EqualTo(HostedRunnerStorageStates.MeasurementFailed));
        });
    }

    public static async Task AssertSuccessfulStorageMeasurementAsync(
        CallbackFixture fixture,
        InfrastructureCommandResultResponse result)
    {
        var verification = await fixture.Db.HostedRunnerStorageVerifications.SingleAsync();
        var persistedDeployment = await fixture.Db.HostedRunnerDeployments.SingleAsync();
        var persistedRunner = await fixture.Db.HostedRunners.SingleAsync();
        var storageAllocation = await fixture.Db.AccountStorageAllocations.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(InfrastructureCommandResultOutcomes.Accepted));
            Assert.That(verification.Status, Is.EqualTo(StorageVerificationStatuses.Succeeded));
            Assert.That(verification.CompletedAtUtc, Is.Not.Null);
            Assert.That(persistedDeployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Succeeded));
            Assert.That(persistedRunner.StorageMeasurementStatus, Is.EqualTo(StorageMeasurementStatuses.Succeeded));
            Assert.That(storageAllocation.StorageFilesystemUuid, Is.EqualTo("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"));
        });
    }

    private static Runner CreateInfrastructureRunner(Guid apiKeyId) => new()
    {
        Id = Guid.NewGuid(),
        ApiKeyId = apiKeyId,
        Name = "infra",
        Purpose = RunnerPurposes.Infrastructure
    };

    private static RunnerHost CreateRunnerHost(Runner runner) => new()
    {
        Id = Guid.NewGuid(),
        Name = "host",
        Provider = "test",
        InfrastructureRunnerId = runner.Id,
        InfrastructureRunner = runner
    };

    private static HostedRunner CreateHostedRunner(
        RunnerHost host,
        string status,
        string? storageState,
        long storageAllowanceBytes,
        int? runtimePort) => new()
    {
        Id = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Name = "hosted",
        ContainerName = "hosted",
        RunnerHostId = host.Id,
        Status = status,
        StorageState = storageState ?? HostedRunnerStorageStates.Unknown,
        StorageAllowanceBytes = storageAllowanceBytes,
        StorageProjectId = 1_000_001,
        RuntimePort = runtimePort
    };

    private static HostedRunnerDeployment CreateDeployment(HostedRunner hosted, string action, string status) => new()
    {
        Id = Guid.NewGuid(),
        HostedRunnerId = hosted.Id,
        HostedRunner = hosted,
        Action = action,
        Status = status
    };

    private static HostedRunnerStorageVerification CreateStorageVerification(Guid deploymentId, Guid hostedRunnerId) => new()
    {
        Id = deploymentId,
        HostedRunnerId = hostedRunnerId,
        IdempotencyKey = "verification",
        Status = StorageVerificationStatuses.Dispatching,
        StartedAtUtc = DateTime.UtcNow,
        Reason = "AdministratorRequested"
    };

    private static InfrastructureActionProvisioningHandler CreateStorageProvisioningHandler(ApplicationDbContext db)
        => new(db, new NoOpAuditEventService(), Options.Create(new HostedRunnerOptions()),
            Options.Create(new StorageTelemetryOptions()), null!);

    private static InfrastructureCommandCallbackService CreateService(
        ApplicationDbContext db,
        InfrastructureActionProvisioningHandler? lifecycle)
    {
        var handler = lifecycle ?? new InfrastructureActionProvisioningHandler(
            db,
            new NoOpAuditEventService(),
            Options.Create(new HostedRunnerOptions()),
            Options.Create(new StorageTelemetryOptions()), null!);
        var resultService = new InfrastructureCommandResultService(db, null!, handler);
        return new InfrastructureCommandCallbackService(db, null!, handler, resultService);
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message,
            object? metadata = null, string outcome = "Success", bool failOnError = false,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

internal sealed record CallbackFixture(
    ApplicationDbContext Db,
    Guid ApiKeyId,
    Runner Runner,
    RunnerHost Host,
    HostedRunner HostedRunner,
    HostedRunnerDeployment Deployment,
    InfrastructureCommandCallbackService Service) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
