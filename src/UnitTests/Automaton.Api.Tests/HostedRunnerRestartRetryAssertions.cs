using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Automaton.Api.Tests;

internal static class HostedRunnerRestartRetryAssertions
{
    public static async Task AssertFailureRollsBackThenRetryCompletesAsync(ApplicationDbContext db, Runner runner)
    {
        var runnerService = HostedRunnerProfileTestServiceFactory.Create(db, null!);
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            runnerService.CompleteHostedRunnerRestartAsync(runner, CancellationToken.None));

        db.ChangeTracker.Clear();
        await AssertStateAsync(db, HostedRunnerDeploymentStatuses.WaitingForReconnect, HostedRunnerStatuses.Failed);

        await runnerService.CompleteHostedRunnerRestartAsync(runner, CancellationToken.None);
        db.ChangeTracker.Clear();
        await AssertStateAsync(db, HostedRunnerDeploymentStatuses.Succeeded, HostedRunnerStatuses.Running);
    }

    private static async Task AssertStateAsync(
        ApplicationDbContext db,
        string expectedDeploymentStatus,
        string expectedRunnerStatus)
    {
        Assert.That(await db.HostedRunnerDeployments.Select(x => x.Status).SingleAsync(), Is.EqualTo(expectedDeploymentStatus));
        Assert.That(await db.HostedRunners.Select(x => x.Status).SingleAsync(), Is.EqualTo(expectedRunnerStatus));
    }
}
