using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class InfrastructureCommandCallbackServiceTests
{
    [Test]
    public async Task FailedResultIsAppliedOnceAndConflictsAreRejected()
    {
        await using var db = CreateDb();
        var apiKeyId = Guid.NewGuid();
        var runner = new Runner { Id = Guid.NewGuid(), ApiKeyId = apiKeyId, Name = "infra", Purpose = RunnerPurposes.Infrastructure };
        var host = new RunnerHost { Id = Guid.NewGuid(), Name = "host", Provider = "test", InfrastructureRunnerId = runner.Id, InfrastructureRunner = runner };
        var hosted = new HostedRunner { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "hosted", ContainerName = "hosted", RunnerHostId = host.Id, Status = HostedRunnerStatuses.Provisioning };
        var deployment = new HostedRunnerDeployment { Id = Guid.NewGuid(), HostedRunnerId = hosted.Id, HostedRunner = hosted, Action = HostedRunnerDeploymentActions.Restart, Status = HostedRunnerDeploymentStatuses.Queued };
        db.AddRange(runner, host, hosted, deployment);
        await db.SaveChangesAsync();
        var service = new InfrastructureCommandCallbackService(db, null!, null!);
        var request = Request(deployment.Id, host.Id, runner.Id, "failed");

        var first = await service.ApplyResultAsync(apiKeyId, deployment.Id, request, CancellationToken.None);
        var duplicate = await service.ApplyResultAsync(apiKeyId, deployment.Id, request, CancellationToken.None);

        Assert.That(first.AlreadyCompleted, Is.False);
        Assert.That(duplicate.AlreadyCompleted, Is.True);
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
        Assert.That((await db.AppModuleExecutions.SingleAsync()).Id, Is.EqualTo(deployment.Id));
        Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyResultAsync(apiKeyId, deployment.Id,
            Request(deployment.Id, host.Id, runner.Id, "different failure"), CancellationToken.None));
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

        var service = new InfrastructureCommandCallbackService(db, null!, null!);
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

        var service = new InfrastructureCommandCallbackService(db, null!, null!);
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
        var service = new InfrastructureCommandCallbackService(db, null!, null!);
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
        var service = new InfrastructureCommandCallbackService(db, null!, null!);
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
        var service = new InfrastructureCommandCallbackService(db, null!, null!);
        var secrets = new InfrastructureCommandSecretsRequest(deployment.Id, host.Id, runner.Id, "reprovision-hosted-runner", []);
        var result = Request(deployment.Id, host.Id, runner.Id, "failed") with { ActionKey = "reprovision-hosted-runner" };
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ResolveSecretsAsync(apiKeyId, deployment.Id, secrets, CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyResultAsync(apiKeyId, deployment.Id, result, CancellationToken.None));
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
    }

    private static InfrastructureCommandResultRequest Request(Guid operationId, Guid hostId, Guid runnerId, string failure) => new(
        operationId, hostId, runnerId, "restart-hosted-runner", [],
        new AppActionExecutionResult { ExecutionId = operationId, Status = AppModuleExecutionStatuses.Failed, FailureSummary = failure, StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow },
        new AppModuleExecutionArtifactResponse(false, null, null, null));

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

}
