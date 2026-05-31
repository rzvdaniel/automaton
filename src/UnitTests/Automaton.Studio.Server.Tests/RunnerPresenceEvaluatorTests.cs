using Automaton.Core.Enums;
using Automaton.Studio.Server.Services;
using NUnit.Framework;

namespace Automaton.Studio.Server.Tests;

public class RunnerPresenceEvaluatorTests
{
    [Test]
    public void DeriveStatus_ReturnsUnknown_WhenHeartbeatNeverTracked()
    {
        var status = RunnerPresenceEvaluator.DeriveStatus(null, new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc));

        Assert.That(status, Is.EqualTo(RunnerStatus.None));
    }

    [Test]
    public void DeriveStatus_ReturnsOnline_WhenHeartbeatIsWithinThreshold()
    {
        var now = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc);

        var status = RunnerPresenceEvaluator.DeriveStatus(now.AddMinutes(-5), now);

        Assert.That(status, Is.EqualTo(RunnerStatus.Online));
    }

    [Test]
    public void DeriveStatus_ReturnsOffline_WhenHeartbeatIsOlderThanThreshold()
    {
        var now = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc);

        var status = RunnerPresenceEvaluator.DeriveStatus(now.AddMinutes(-11), now);

        Assert.That(status, Is.EqualTo(RunnerStatus.Offline));
    }
}