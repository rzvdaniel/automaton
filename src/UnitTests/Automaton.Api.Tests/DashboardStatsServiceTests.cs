using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;

namespace Automaton.Api.Tests;

[TestFixture]
public class DashboardStatsServiceTests
{
    [Test]
    public void GetAppOwnerRunnerMetrics_ExcludesInfrastructureFromBothCounts()
    {
        var metrics = DashboardStatsService.GetAppOwnerRunnerMetrics(
        [
            CreateRunner(RunnerPurposes.Infrastructure, RunnerStatus.Online),
            CreateRunner(RunnerPurposes.Managed, RunnerStatus.Online),
            CreateRunner(RunnerPurposes.Managed, RunnerStatus.Offline)
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(metrics.Total, Is.EqualTo(2));
            Assert.That(metrics.Online, Is.EqualTo(1));
        });
    }

    [Test]
    public void GetAppOwnerRunnerMetrics_IncludesHostedAndSelfHostedRunners()
    {
        var metrics = DashboardStatsService.GetAppOwnerRunnerMetrics(
        [
            CreateRunner(RunnerPurposes.Managed, RunnerStatus.Online, isHostedRunner: true),
            CreateRunner(RunnerPurposes.Managed, RunnerStatus.Offline, isHostedRunner: false)
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(metrics.Total, Is.EqualTo(2));
            Assert.That(metrics.Online, Is.EqualTo(1));
        });
    }

    private static RunnerDetails CreateRunner(string purpose, RunnerStatus status, bool isHostedRunner = false)
        => new()
        {
            Purpose = purpose,
            Status = status,
            IsHostedRunner = isHostedRunner
        };
}
