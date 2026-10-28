using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;

namespace Automaton.Api.Tests;

[TestFixture]
public class DashboardStatsServiceTests
{
    [Test]
    public void GetAppOwnerRunnerMetrics_ExcludesUnsupportedProfilesFromBothCounts()
    {
        var metrics = DashboardStatsService.GetAppOwnerRunnerMetrics(
        [
            CreateRunner("Infrastructure", RunnerStatus.Online),
            CreateRunner(RunnerPurposes.Managed, RunnerStatus.Online, RunnerApplicationProfiles.MachineManagement),
            CreateRunner(RunnerPurposes.Managed, RunnerStatus.Offline, RunnerApplicationProfiles.MachineManagement)
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(metrics.Total, Is.EqualTo(2));
            Assert.That(metrics.Online, Is.EqualTo(1));
        });
    }

    [Test]
    public void GetAppOwnerRunnerMetrics_ExcludesRetiredHostedRunners()
    {
        var metrics = DashboardStatsService.GetAppOwnerRunnerMetrics(
        [
            CreateRunner(RunnerPurposes.Managed, RunnerStatus.Online, RunnerApplicationProfiles.MachineManagement, isHostedRunner: true),
            CreateRunner(RunnerPurposes.Managed, RunnerStatus.Offline, RunnerApplicationProfiles.MachineManagement)
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(metrics.Total, Is.EqualTo(1));
            Assert.That(metrics.Online, Is.EqualTo(0));
        });
    }

    private static RunnerDetails CreateRunner(
        string purpose,
        RunnerStatus status,
        string? profile = null,
        bool isHostedRunner = false)
        => new()
        {
            Purpose = purpose,
            ApplicationProfile = profile,
            Status = status,
            IsHostedRunner = isHostedRunner
        };
}
