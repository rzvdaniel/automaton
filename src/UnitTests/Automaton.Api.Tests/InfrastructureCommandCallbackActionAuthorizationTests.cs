using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class InfrastructureCommandCallbackActionAuthorizationTests
{
    [Test]
    public async Task ResultRejectsAnotherRunnerApiKey()
    {
        await using var fixture = await InfrastructureCommandCallbackTestScenarios.CreateAsync();

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ApplyResultAsync(
            Guid.NewGuid(),
            fixture.Deployment.Id,
            CreateFailedResult(fixture, "restart-hosted-runner"),
            CancellationToken.None));
    }

    [TestCase(HostedRunnerDeploymentActions.Restart, "restart-hosted-runner")]
    [TestCase(HostedRunnerDeploymentActions.Deprovision, "delete-hosted-runner")]
    [TestCase(HostedRunnerDeploymentActions.VerifyStorage, "measure-hosted-runner-storage")]
    public void DeploymentActionMapsOnlyToItsTrustedAction(string deploymentAction, string actionKey)
        => Assert.That(
            InfrastructureCommandCallbackService.TrustedActionKeyForDeployment(deploymentAction),
            Is.EqualTo(actionKey));

    [TestCase(HostedRunnerDeploymentActions.Provision)]
    [TestCase(HostedRunnerDeploymentActions.Reprovision)]
    [TestCase(HostedRunnerDeploymentActions.Suspend)]
    [TestCase(HostedRunnerDeploymentActions.Resume)]
    public void RetiredActionsNoLongerHaveTrustedCallbacks(string deploymentAction)
        => Assert.That(InfrastructureCommandCallbackService.TrustedActionKeyForDeployment(deploymentAction), Is.Null);

    [Test]
    public async Task MismatchedActionIsRejectedForBothSecretsAndResultsWithoutMutation()
    {
        await using var fixture = await InfrastructureCommandCallbackTestScenarios.CreateAsync(
            hostedRunnerStatus: HostedRunnerStatuses.Running,
            deploymentAction: HostedRunnerDeploymentActions.Restart,
            deploymentStatus: HostedRunnerDeploymentStatuses.Queued);

        await InfrastructureCommandCallbackSecurityScenarios.AssertMismatchedCallbacksAreRejectedAsync(fixture);
    }

    [TestCase(HostedRunnerDeploymentActions.Suspend, "suspend-hosted-runner")]
    [TestCase(HostedRunnerDeploymentActions.Resume, "resume-hosted-runner")]
    public async Task RetiredStorageCommandCannotResolveSecretsOrApplyResults(string action, string key)
    {
        await using var fixture = await InfrastructureCommandCallbackTestScenarios.CreateAsync(
            hostedRunnerStatus: HostedRunnerStatuses.Running, deploymentAction: action);
        await InfrastructureCommandCallbackSecurityScenarios.AssertCallbacksAreRejectedAsync(fixture, key);
    }

    [Test]
    public async Task UnknownDeploymentActionFailsClosed()
    {
        await using var fixture = await InfrastructureCommandCallbackTestScenarios.CreateAsync(
            deploymentAction: "unknown-action");

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ApplyResultAsync(
            fixture.ApiKeyId,
            fixture.Deployment.Id,
            CreateFailedResult(fixture, "restart-hosted-runner"),
            CancellationToken.None));
        Assert.That(await fixture.Db.AppModuleExecutions.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task ProvisionOperationRejectsReprovisionCallback()
    {
        await using var fixture = await InfrastructureCommandCallbackTestScenarios.CreateAsync(
            deploymentAction: HostedRunnerDeploymentActions.Provision);
        var secrets = new InfrastructureCommandSecretsRequest(
            fixture.Deployment.Id,
            fixture.Host.Id,
            fixture.Runner.Id,
            "reprovision-hosted-runner",
            []);

        Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ResolveSecretsAsync(
            fixture.ApiKeyId, fixture.Deployment.Id, secrets, CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ApplyResultAsync(
            fixture.ApiKeyId,
            fixture.Deployment.Id,
            CreateFailedResult(fixture, "reprovision-hosted-runner"),
            CancellationToken.None));
        Assert.That(await fixture.Db.AppModuleExecutions.CountAsync(), Is.Zero);
    }

    private static InfrastructureCommandResultRequest CreateFailedResult(CallbackFixture fixture, string actionKey)
        => new(
            fixture.Deployment.Id,
            fixture.Host.Id,
            fixture.Runner.Id,
            actionKey,
            [],
            new AppActionExecutionResult
            {
                ExecutionId = fixture.Deployment.Id,
                Status = AppModuleExecutionStatuses.Failed,
                FailureSummary = "failed",
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow
            },
            new AppModuleExecutionArtifactResponse(false, null, null, null));
}
