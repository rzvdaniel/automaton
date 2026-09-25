using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Config;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Api.Services.Interfaces;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Messaging.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class InfrastructureCommandCallbackServiceTests
{
    [Test]
    public async Task FailedResultIsAppliedOnceAndConflictsBecomeTerminalStale()
    {
        await using var fixture = await CreateCallbackFixtureAsync();
        var db = fixture.Db;
        var deployment = fixture.Deployment;
        var service = fixture.Service;
        var request = Request(deployment.Id, fixture.Host.Id, fixture.Runner.Id, "failed");

        var first = await service.ApplyResultAsync(fixture.ApiKeyId, deployment.Id, request, CancellationToken.None);
        var duplicate = await service.ApplyResultAsync(fixture.ApiKeyId, deployment.Id, request, CancellationToken.None);

        Assert.That(first.Outcome, Is.EqualTo(InfrastructureCommandResultOutcomes.Accepted));
        Assert.That(duplicate.Outcome, Is.EqualTo(InfrastructureCommandResultOutcomes.AlreadyApplied));
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
        Assert.That((await db.AppModuleExecutions.SingleAsync()).Id, Is.EqualTo(deployment.Id));
        var stale = await service.ApplyResultAsync(fixture.ApiKeyId, deployment.Id,
            Request(deployment.Id, fixture.Host.Id, fixture.Runner.Id, "different failure"), CancellationToken.None);
        Assert.That(stale.Outcome, Is.EqualTo(InfrastructureCommandResultOutcomes.TerminalStale));
    }

    [Test]
    public async Task FailedStorageMeasurementTerminalizesVerificationWithoutFailingRunnerLifecycle()
    {
        await using var fixture = await CreateCallbackFixtureAsync(
            hostedRunnerStatus: HostedRunnerStatuses.Running,
            deploymentAction: HostedRunnerDeploymentActions.VerifyStorage,
            deploymentStatus: HostedRunnerDeploymentStatuses.Dispatching,
            storageState: HostedRunnerStorageStates.Healthy,
            addStorageVerification: true);
        var result = await fixture.Service.ApplyResultAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            Request(fixture.Deployment.Id, fixture.Host.Id, fixture.Runner.Id, "expected volume is not provisioned") with
            { ActionKey = StorageTelemetryActions.MeasureHostedRunnerStorage }, CancellationToken.None);

        await AssertFailedStorageMeasurementAsync(fixture, result);
    }

    [Test]
    public async Task SuccessfulStorageMeasurementCompletesVerification()
    {
        await using var fixture = await CreateCallbackFixtureAsync(
            hostedRunnerStatus: HostedRunnerStatuses.Running,
            deploymentAction: HostedRunnerDeploymentActions.VerifyStorage,
            deploymentStatus: HostedRunnerDeploymentStatuses.Dispatching,
            storageAllowanceBytes: 1_000,
            addStorageVerification: true,
            completeStorageLifecycle: true);
        var result = await fixture.Service.ApplyResultAsync(
            fixture.ApiKeyId,
            fixture.Deployment.Id,
            CreateSuccessfulStorageMeasurementResult(fixture),
            CancellationToken.None);
        await AssertSuccessfulStorageMeasurementAsync(fixture, result);
    }

    [Test]
    public async Task TerminalDeploymentWithoutExecutionIsTerminalStaleWithoutMutation()
    {
        await using var fixture = await CreateCallbackFixtureAsync(
            hostedRunnerStatus: HostedRunnerStatuses.Failed,
            deploymentAction: HostedRunnerDeploymentActions.Reprovision,
            deploymentStatus: HostedRunnerDeploymentStatuses.Failed);
        var db = fixture.Db;
        var result = await fixture.Service.ApplyResultAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            Request(fixture.Deployment.Id, fixture.Host.Id, fixture.Runner.Id, "late result") with { ActionKey = "reprovision-hosted-runner" },
            CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(InfrastructureCommandResultOutcomes.TerminalStale));
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
    }

    [Test]
    public async Task ResultRejectsAnotherRunnerApiKey()
    {
        await using var db = CreateDb();
        var runner = new Runner { Id = Guid.NewGuid(), ApiKeyId = Guid.NewGuid(), Name = "infra", Purpose = RunnerPurposes.Infrastructure };
        var host = new RunnerHost { Id = Guid.NewGuid(), Name = "host", Provider = "test", InfrastructureRunnerId = runner.Id, InfrastructureRunner = runner };
        var hosted = new HostedRunner { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted", RunnerHostId = host.Id };
        var deployment = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = hosted.Id, HostedRunner = hosted };
        db.AddRange(runner, host, hosted, deployment);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyResultAsync(Guid.NewGuid(), deployment.Id,
            Request(deployment.Id, host.Id, runner.Id, "failed"), CancellationToken.None));
    }

    [Test]
    public async Task FailedHostedRunnerDeleteRetainsRuntimePort()
    {
        await using var db = CreateDb();
        var apiKeyId = Guid.NewGuid();
        var runner = new Runner { Id = Guid.NewGuid(), ApiKeyId = apiKeyId, Name = "infra", Purpose = RunnerPurposes.Infrastructure };
        var host = new RunnerHost { Id = Guid.NewGuid(), Name = "host", Provider = "test", InfrastructureRunnerId = runner.Id, InfrastructureRunner = runner };
        var hosted = new HostedRunner { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted", RunnerHostId = host.Id, Status = HostedRunnerStatuses.Deleting, RuntimePort = 20000 };
        var deployment = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = hosted.Id, HostedRunner = hosted, Action = HostedRunnerDeploymentActions.Deprovision, Status = HostedRunnerDeploymentStatuses.Executing };
        db.AddRange(runner, host, hosted, deployment);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.ApplyResultAsync(apiKeyId, deployment.Id,
            Request(deployment.Id, host.Id, runner.Id, "delete failed") with { ActionKey = "delete-hosted-runner" },
            CancellationToken.None);

        Assert.That((await db.HostedRunners.SingleAsync()).RuntimePort, Is.EqualTo(20000));
        Assert.That((await db.HostedRunners.SingleAsync()).Status, Is.EqualTo(HostedRunnerStatuses.DeleteFailed));
    }


    [TestCase(HostedRunnerDeploymentActions.Restart, "restart-hosted-runner")]
    [TestCase(HostedRunnerDeploymentActions.Provision, "provision-hosted-runner")]
    [TestCase(HostedRunnerDeploymentActions.Reprovision, "reprovision-hosted-runner")]
    [TestCase(HostedRunnerDeploymentActions.Suspend, "suspend-hosted-runner")]
    [TestCase(HostedRunnerDeploymentActions.Resume, "resume-hosted-runner")]
    [TestCase(HostedRunnerDeploymentActions.Deprovision, "delete-hosted-runner")]
    [TestCase(HostedRunnerDeploymentActions.VerifyStorage, "measure-hosted-runner-storage")]
    public void DeploymentActionMapsOnlyToItsTrustedAction(string deploymentAction, string actionKey)
        => Assert.That(InfrastructureCommandCallbackService.TrustedActionKeyForDeployment(deploymentAction), Is.EqualTo(actionKey));

    [Test]
    public async Task MismatchedActionIsRejectedForBothSecretsAndResultsWithoutMutation()
    {
        await using var db = CreateDb();
        var apiKeyId = Guid.NewGuid();
        var runner = new Runner { Id = Guid.NewGuid(), ApiKeyId = apiKeyId, Name = "infra", Purpose = RunnerPurposes.Infrastructure };
        var host = new RunnerHost { Id = Guid.NewGuid(), Name = "host", Provider = "test", InfrastructureRunnerId = runner.Id, InfrastructureRunner = runner };
        var hosted = new HostedRunner { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted", RunnerHostId = host.Id, Status = HostedRunnerStatuses.Running };
        var deployment = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = hosted.Id, HostedRunner = hosted, Action = HostedRunnerDeploymentActions.Restart, Status = HostedRunnerDeploymentStatuses.Queued };
        db.AddRange(runner, host, hosted, deployment);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var wrongSecrets = new InfrastructureCommandSecretsRequest(deployment.Id, host.Id, runner.Id, "delete-hosted-runner", []);
        var wrongResult = Request(deployment.Id, host.Id, runner.Id, "failed") with { ActionKey = "delete-hosted-runner" };

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ResolveSecretsAsync(apiKeyId, deployment.Id, wrongSecrets, CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyResultAsync(apiKeyId, deployment.Id, wrongResult, CancellationToken.None));
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Queued));
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task UnknownDeploymentActionFailsClosed()
    {
        await using var db = CreateDb();
        var apiKeyId = Guid.NewGuid();
        var runner = new Runner { Id = Guid.NewGuid(), ApiKeyId = apiKeyId, Name = "infra", Purpose = RunnerPurposes.Infrastructure };
        var host = new RunnerHost { Id = Guid.NewGuid(), Name = "host", Provider = "test", InfrastructureRunnerId = runner.Id, InfrastructureRunner = runner };
        var hosted = new HostedRunner { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted", RunnerHostId = host.Id };
        var deployment = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = hosted.Id, HostedRunner = hosted, Action = "unknown-action" };
        db.AddRange(runner, host, hosted, deployment);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyResultAsync(apiKeyId, deployment.Id,
            Request(deployment.Id, host.Id, runner.Id, "failed"), CancellationToken.None));
    }

    [Test]
    public async Task ProvisionOperationRejectsReprovisionCallback()
    {
        await using var db = CreateDb();
        var apiKeyId = Guid.NewGuid();
        var runner = new Runner { Id = Guid.NewGuid(), ApiKeyId = apiKeyId, Name = "infra", Purpose = RunnerPurposes.Infrastructure };
        var host = new RunnerHost { Id = Guid.NewGuid(), Name = "host", Provider = "test", InfrastructureRunnerId = runner.Id, InfrastructureRunner = runner };
        var hosted = new HostedRunner { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted", RunnerHostId = host.Id };
        var deployment = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = hosted.Id, HostedRunner = hosted, Action = HostedRunnerDeploymentActions.Provision };
        db.AddRange(runner, host, hosted, deployment);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var secrets = new InfrastructureCommandSecretsRequest(deployment.Id, host.Id, runner.Id, "reprovision-hosted-runner", []);
        var result = Request(deployment.Id, host.Id, runner.Id, "failed") with { ActionKey = "reprovision-hosted-runner" };
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ResolveSecretsAsync(apiKeyId, deployment.Id, secrets, CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyResultAsync(apiKeyId, deployment.Id, result, CancellationToken.None));
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task ValidGrantReturnsExecutableDispositionAndSecrets()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db);

        var response = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            SecretsRequest(fixture, fixture.GrantId), CancellationToken.None);

        Assert.That(response.Disposition, Is.EqualTo(InfrastructureCommandSecretResolutionDispositions.Executable));
        Assert.That(response.EnvironmentVariables["AUTOMATON_TOKEN"], Is.EqualTo("secret-value"));
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Status,
            Is.EqualTo(HostedRunnerDeploymentStatuses.Dispatching));
    }

    [Test]
    public async Task TerminalDeploymentReturnsExistingTerminalReasonWithoutMutation()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db, HostedRunnerDeploymentStatuses.Failed);
        var deployment = await db.HostedRunnerDeployments.SingleAsync();
        deployment.Error = HostedRunnerDeploymentErrors.AmbiguousDispatch;
        deployment.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var response = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            SecretsRequest(fixture, Guid.NewGuid()), CancellationToken.None);

        Assert.That(response.Disposition, Is.EqualTo(InfrastructureCommandSecretResolutionDispositions.Terminal));
        Assert.That(response.TerminalReason, Is.EqualTo(HostedRunnerDeploymentErrors.AmbiguousDispatch));
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).FinishedAt, Is.Not.Null);
    }

    [TestCase(false, false, HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch)]
    [TestCase(true, false, HostedRunnerDeploymentErrors.AmbiguousDispatch)]
    [TestCase(false, true, HostedRunnerDeploymentErrors.AmbiguousDispatch)]
    public async Task UnusableGrantTerminalizesWithEvidenceAwareReason(
        bool delivered,
        bool consumed,
        string expectedReason)
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db, HostedRunnerDeploymentStatuses.Dispatching,
            expired: true, delivered: delivered, consumed: consumed);

        var response = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            SecretsRequest(fixture, fixture.GrantId), CancellationToken.None);
        var deployment = await db.HostedRunnerDeployments.Include(x => x.HostedRunner).SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.Disposition, Is.EqualTo(InfrastructureCommandSecretResolutionDispositions.Terminal));
            Assert.That(response.TerminalReason, Is.EqualTo(expectedReason));
            Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
            Assert.That(deployment.Error, Is.EqualTo(expectedReason));
            Assert.That(deployment.FinishedAt, Is.Not.Null);
            Assert.That(deployment.HostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
        });
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task MissingGrantTerminalizesBeforeDispatch()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db);

        var response = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            SecretsRequest(fixture, Guid.NewGuid()), CancellationToken.None);

        Assert.That(response.Disposition, Is.EqualTo(InfrastructureCommandSecretResolutionDispositions.Terminal));
        Assert.That(response.TerminalReason, Is.EqualTo(HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch));
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Error,
            Is.EqualTo(HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch));
    }

    [Test]
    public async Task RevokedGrantBeforeDeliveryTerminalizesBeforeDispatch()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db);
        var grant = await db.ExecutionSecretGrants.SingleAsync();
        grant.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var response = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            SecretsRequest(fixture, fixture.GrantId), CancellationToken.None);

        Assert.That(response.Disposition, Is.EqualTo(InfrastructureCommandSecretResolutionDispositions.Terminal));
        Assert.That(response.TerminalReason, Is.EqualTo(HostedRunnerDeploymentErrors.SecretGrantUnavailableBeforeDispatch));
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Status,
            Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
    }

    [Test]
    public async Task RepeatedResolutionAfterTerminalizationReturnsSameDispositionAndReason()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db, HostedRunnerDeploymentStatuses.Dispatching,
            expired: true, delivered: true);
        var request = SecretsRequest(fixture, fixture.GrantId);

        var first = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id, request, CancellationToken.None);
        var second = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id, request, CancellationToken.None);

        Assert.That(second.Disposition, Is.EqualTo(first.Disposition));
        Assert.That(second.TerminalReason, Is.EqualTo(first.TerminalReason));
    }

    [Test]
    public async Task DeliveredSingleDeliveryGrantWithoutReadCountTerminalizesAsAmbiguousDispatch()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db, HostedRunnerDeploymentStatuses.Dispatching,
            expired: true, delivered: true);
        var grant = await db.ExecutionSecretGrants.SingleAsync();
        grant.ReadCount = 0;
        grant.ConsumedAtUtc = null;
        await db.SaveChangesAsync();

        var response = await fixture.Service.ResolveSecretsAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            SecretsRequest(fixture, fixture.GrantId), CancellationToken.None);
        var deployment = await db.HostedRunnerDeployments.Include(x => x.HostedRunner).SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.Disposition, Is.EqualTo(InfrastructureCommandSecretResolutionDispositions.Terminal));
            Assert.That(response.TerminalReason, Is.EqualTo(HostedRunnerDeploymentErrors.AmbiguousDispatch));
            Assert.That(deployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
            Assert.That(deployment.Error, Is.EqualTo(HostedRunnerDeploymentErrors.AmbiguousDispatch));
            Assert.That(deployment.FinishedAt, Is.Not.Null);
            Assert.That(deployment.HostedRunner.Status, Is.EqualTo(HostedRunnerStatuses.Failed));
        });
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task TerminalResponseRequiresPersistedTerminalDeployment()
    {
        await using var db = CreateDb();
        var fixture = await CreateResolutionFixture(db, HostedRunnerDeploymentStatuses.Dispatching, expired: true,
            transactionalEventExecutor: new NoOpTransactionalEventExecutor());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ResolveSecretsAsync(
            fixture.ApiKeyId, fixture.Deployment.Id, SecretsRequest(fixture, fixture.GrantId), CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("could not be confirmed as durably persisted"));
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Status,
            Is.EqualTo(HostedRunnerDeploymentStatuses.Dispatching));
    }

    private static InfrastructureCommandSecretsRequest SecretsRequest(
        ResolutionFixture fixture, Guid grantId) => new(
            fixture.Deployment.Id, fixture.Host.Id, fixture.Runner.Id,
            "reprovision-hosted-runner", [grantId]);

    private static async Task<ResolutionFixture> CreateResolutionFixture(
        ApplicationDbContext db,
        string status = HostedRunnerDeploymentStatuses.Dispatching,
        bool expired = false,
        bool delivered = false,
        bool consumed = false,
        ITransactionalEventExecutor? transactionalEventExecutor = null)
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var store = new SecretStore(db, protector);
        var grants = new ExecutionSecretGrantService(db, protector);
        var apiKeyId = Guid.NewGuid();
        var runner = new Runner { Id = Guid.NewGuid(), ApiKeyId = apiKeyId, Name = "infra", Purpose = RunnerPurposes.Infrastructure };
        var host = new RunnerHost { Id = Guid.NewGuid(), Name = "host", Provider = "test", InfrastructureRunnerId = runner.Id, InfrastructureRunner = runner };
        var hosted = new HostedRunner { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted", RunnerHostId = host.Id, Status = HostedRunnerStatuses.Provisioning };
        var deployment = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = hosted.Id, HostedRunner = hosted, Action = HostedRunnerDeploymentActions.Reprovision, Status = status };
        db.AddRange(runner, host, hosted, deployment);
        await db.SaveChangesAsync();

        var scope = new SecretGrantScope(deployment.Id, InfrastructureAppTrust.PlatformAppId,
            "reprovision-hosted-runner", runner.Id, host.Id, SecretOwnerTypes.HostedRunner, hosted.Id);
        var version = await store.CreateVersionAsync(new SecretOwner(SecretOwnerTypes.HostedRunner, hosted.Id),
            "runner-secret", "secret-value", DateTime.UtcNow.AddMinutes(10), CancellationToken.None);
        var grantId = await grants.CreateGrantAsync(version.SecretVersionId, scope, "AUTOMATON_TOKEN",
            DateTime.UtcNow.AddMinutes(5), CancellationToken.None);
        var grant = await db.ExecutionSecretGrants.SingleAsync();
        if (expired) grant.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        if (delivered) grant.DeliveredAtUtc = DateTime.UtcNow.AddMinutes(-2);
        if (delivered) grant.ReadCount = 1;
        if (consumed)
        {
            grant.LeasedAtUtc = DateTime.UtcNow.AddMinutes(-2);
            grant.ConsumedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        }
        await db.SaveChangesAsync();

        var lifecycle = new InfrastructureActionProvisioningHandler(
            db, new NoOpAuditEventService(), Options.Create(new HostedRunnerOptions()), Options.Create(new StorageTelemetryOptions()));
        return new(apiKeyId, runner, host, hosted, deployment, grantId,
            CreateService(db, grants, lifecycle, transactionalEventExecutor));
    }

    private static InfrastructureCommandResultRequest Request(Guid operationId, Guid hostId, Guid runnerId, string failure) => new(
        operationId, hostId, runnerId, "restart-hosted-runner", [],
        new AppActionExecutionResult { ExecutionId = operationId, Status = AppModuleExecutionStatuses.Failed, FailureSummary = failure, StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow },
        new AppModuleExecutionArtifactResponse(false, null, null, null));

    private static InfrastructureCommandCallbackService CreateService(
        ApplicationDbContext db,
        IExecutionSecretGrantService? secretGrants = null,
        InfrastructureActionProvisioningHandler? lifecycle = null,
        ITransactionalEventExecutor? transactionalEventExecutor = null)
    {
        var resultService = new InfrastructureCommandResultService(db, secretGrants!, lifecycle!);
        return new InfrastructureCommandCallbackService(
            db,
            secretGrants!,
            lifecycle!,
            resultService,
            transactionalEventExecutor);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private static async Task<CallbackFixture> CreateCallbackFixtureAsync(
        string hostedRunnerStatus = HostedRunnerStatuses.Provisioning,
        string deploymentAction = HostedRunnerDeploymentActions.Restart,
        string deploymentStatus = HostedRunnerDeploymentStatuses.Queued,
        string? storageState = null,
        bool addStorageVerification = false,
        long storageAllowanceBytes = 0,
        bool completeStorageLifecycle = false)
    {
        var db = CreateDb();
        var apiKeyId = Guid.NewGuid();
        var runner = new Runner
        {
            Id = Guid.NewGuid(), ApiKeyId = apiKeyId, Name = "infra", Purpose = RunnerPurposes.Infrastructure
        };
        var host = new RunnerHost
        {
            Id = Guid.NewGuid(), Name = "host", Provider = "test",
            InfrastructureRunnerId = runner.Id, InfrastructureRunner = runner
        };
        var hosted = new HostedRunner
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted",
            RunnerHostId = host.Id, Status = hostedRunnerStatus,
            StorageState = storageState ?? HostedRunnerStorageStates.Unknown,
            StorageAllowanceBytes = storageAllowanceBytes
        };
        var deployment = new HostedRunnerDeployment
        {
            Id = Guid.NewGuid(), HostedRunnerId = hosted.Id, HostedRunner = hosted,
            Action = deploymentAction, Status = deploymentStatus
        };
        db.AddRange(runner, host, hosted, deployment);
        if (addStorageVerification)
        {
            db.HostedRunnerStorageVerifications.Add(new HostedRunnerStorageVerification
            {
                Id = deployment.Id, HostedRunnerId = hosted.Id, IdempotencyKey = "verification",
                Status = StorageVerificationStatuses.Dispatching, StartedAtUtc = DateTime.UtcNow,
                Reason = "AdministratorRequested"
            });
        }

        await db.SaveChangesAsync();
        var lifecycle = completeStorageLifecycle ? CreateStorageProvisioningHandler(db) : null;
        return new CallbackFixture(db, apiKeyId, runner, host, hosted, deployment,
            CreateService(db, lifecycle: lifecycle));
    }

    private static InfrastructureActionProvisioningHandler CreateStorageProvisioningHandler(ApplicationDbContext db)
        => new(db, new NoOpAuditEventService(), Options.Create(new HostedRunnerOptions()),
            Options.Create(new StorageTelemetryOptions()));

    private static InfrastructureCommandResultRequest CreateSuccessfulStorageMeasurementResult(CallbackFixture fixture)
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
                "{\"success\":true,\"systemBytes\":0,\"userBytes\":100,\"totalBytes\":100,\"usageSource\":\"xfs-project-quota\"}",
                null,
                null));

    private static async Task AssertFailedStorageMeasurementAsync(
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

    private static async Task AssertSuccessfulStorageMeasurementAsync(
        CallbackFixture fixture,
        InfrastructureCommandResultResponse result)
    {
        var verification = await fixture.Db.HostedRunnerStorageVerifications.SingleAsync();
        var persistedDeployment = await fixture.Db.HostedRunnerDeployments.SingleAsync();
        var persistedRunner = await fixture.Db.HostedRunners.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(InfrastructureCommandResultOutcomes.Accepted));
            Assert.That(verification.Status, Is.EqualTo(StorageVerificationStatuses.Succeeded));
            Assert.That(verification.CompletedAtUtc, Is.Not.Null);
            Assert.That(persistedDeployment.Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Succeeded));
            Assert.That(persistedRunner.StorageMeasurementStatus, Is.EqualTo(StorageMeasurementStatuses.Succeeded));
        });
    }

    private sealed record CallbackFixture(
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

    private sealed record ResolutionFixture(
        Guid ApiKeyId,
        Runner Runner,
        RunnerHost Host,
        HostedRunner HostedRunner,
        HostedRunnerDeployment Deployment,
        Guid GrantId,
        InfrastructureCommandCallbackService Service);

    private sealed class NoOpAuditEventService : IAuditEventService
    {
        public Task WriteAsync(AuditEventWriteRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteAsync(string action, string entityType, Guid? entityId, string message, object? metadata = null,
            string outcome = "Success", bool failOnError = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpTransactionalEventExecutor : ITransactionalEventExecutor
    {
        public Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

}
