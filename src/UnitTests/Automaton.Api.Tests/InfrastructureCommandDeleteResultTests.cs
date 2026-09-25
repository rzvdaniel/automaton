using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class InfrastructureCommandDeleteResultTests
{
    [Test]
    public async Task FailedHostedRunnerDeleteRetainsRuntimePort()
    {
        await using var fixture = await InfrastructureCommandCallbackTestScenarios.CreateAsync(
            hostedRunnerStatus: HostedRunnerStatuses.Deleting,
            deploymentAction: HostedRunnerDeploymentActions.Deprovision,
            deploymentStatus: HostedRunnerDeploymentStatuses.Executing,
            runtimePort: 20000);

        await fixture.Service.ApplyResultAsync(
            fixture.ApiKeyId,
            fixture.Deployment.Id,
            CreateFailedDeleteResult(fixture),
            CancellationToken.None);

        var persistedRunner = await fixture.Db.HostedRunners.SingleAsync();
        Assert.That(persistedRunner.RuntimePort, Is.EqualTo(20000));
        Assert.That(persistedRunner.Status, Is.EqualTo(HostedRunnerStatuses.DeleteFailed));
    }

    private static InfrastructureCommandResultRequest CreateFailedDeleteResult(CallbackFixture fixture) => new(
        fixture.Deployment.Id,
        fixture.Host.Id,
        fixture.Runner.Id,
        "delete-hosted-runner",
        [],
        new AppActionExecutionResult
        {
            ExecutionId = fixture.Deployment.Id,
            Status = AppModuleExecutionStatuses.Failed,
            FailureSummary = "delete failed",
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow
        },
        new AppModuleExecutionArtifactResponse(false, null, null, null));
}
