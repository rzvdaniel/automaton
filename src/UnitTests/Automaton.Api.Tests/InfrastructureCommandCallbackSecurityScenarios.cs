using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Tests;

internal static class InfrastructureCommandCallbackSecurityScenarios
{
    public static async Task AssertMismatchedCallbacksAreRejectedAsync(CallbackFixture fixture)
    {
        var requests = CreateMismatchedRequests(fixture);
        AssertBothRequestsRejected(fixture, requests);
        await AssertNoCallbackMutationAsync(fixture.Db);
    }

    private static (InfrastructureCommandSecretsRequest Secrets, InfrastructureCommandResultRequest Result)
        CreateMismatchedRequests(CallbackFixture fixture) =>
        (
            new InfrastructureCommandSecretsRequest(
                fixture.Deployment.Id, fixture.Host.Id, fixture.Runner.Id, "delete-hosted-runner", []),
            new InfrastructureCommandResultRequest(
                fixture.Deployment.Id,
                fixture.Host.Id,
                fixture.Runner.Id,
                "delete-hosted-runner",
                [],
                new AppActionExecutionResult
                {
                    ExecutionId = fixture.Deployment.Id,
                    Status = AppModuleExecutionStatuses.Failed,
                    FailureSummary = "failed",
                    StartedAtUtc = DateTime.UtcNow,
                    CompletedAtUtc = DateTime.UtcNow
                },
                new AppModuleExecutionArtifactResponse(false, null, null, null)));

    private static void AssertBothRequestsRejected(
        CallbackFixture fixture,
        (InfrastructureCommandSecretsRequest Secrets, InfrastructureCommandResultRequest Result) requests)
    {
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ResolveSecretsAsync(
            fixture.ApiKeyId, fixture.Deployment.Id, requests.Secrets, CancellationToken.None));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ApplyResultAsync(
            fixture.ApiKeyId, fixture.Deployment.Id, requests.Result, CancellationToken.None));
    }

    private static async Task AssertNoCallbackMutationAsync(ApplicationDbContext db)
    {
        Assert.That((await db.HostedRunnerDeployments.SingleAsync()).Status,
            Is.EqualTo(HostedRunnerDeploymentStatuses.Queued));
        Assert.That(await db.AppModuleExecutions.CountAsync(), Is.Zero);
    }
}
