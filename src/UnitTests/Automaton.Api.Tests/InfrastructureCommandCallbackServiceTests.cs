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

    private static InfrastructureCommandResultRequest Request(Guid operationId, Guid hostId, Guid runnerId, string failure) => new(
        operationId, hostId, runnerId, "restart-hosted-runner", [],
        new AppActionExecutionResult { ExecutionId = operationId, Status = AppModuleExecutionStatuses.Failed, FailureSummary = failure, StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow },
        new AppModuleExecutionArtifactResponse(false, null, null, null));

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
