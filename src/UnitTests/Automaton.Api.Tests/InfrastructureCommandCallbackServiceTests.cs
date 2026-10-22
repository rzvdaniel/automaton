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
    public async Task FailedResultIsAppliedOnceAndConflictsBecomeTerminalStale()
    {
        await using var fixture = await InfrastructureCommandCallbackTestScenarios.CreateAsync();
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
    public async Task TerminalDeploymentWithoutExecutionIsTerminalStaleWithoutMutation()
    {
        await using var fixture = await InfrastructureCommandCallbackTestScenarios.CreateAsync(
            hostedRunnerStatus: HostedRunnerStatuses.Failed,
            deploymentAction: HostedRunnerDeploymentActions.Restart,
            deploymentStatus: HostedRunnerDeploymentStatuses.Failed);
        var db = fixture.Db;
        var result = await fixture.Service.ApplyResultAsync(fixture.ApiKeyId, fixture.Deployment.Id,
            Request(fixture.Deployment.Id, fixture.Host.Id, fixture.Runner.Id, "late result"),
            CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(InfrastructureCommandResultOutcomes.TerminalStale));
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Status, Is.EqualTo(HostedRunnerDeploymentStatuses.Failed));
    }

    private static InfrastructureCommandResultRequest Request(Guid operationId, Guid hostId, Guid runnerId, string failure) => new(
        operationId, hostId, runnerId, "restart-hosted-runner", [],
        new AppActionExecutionResult { ExecutionId = operationId, Status = AppModuleExecutionStatuses.Failed, FailureSummary = failure, StartedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow },
        new AppModuleExecutionArtifactResponse(false, null, null, null));


}
