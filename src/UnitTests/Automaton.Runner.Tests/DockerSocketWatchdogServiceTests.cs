using Automaton.Runner.Console.Services;

using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class DockerSocketWatchdogServiceTests
{
    [Test]
    public void ExitsOnlyAfterConsecutiveFailures()
    {
        var counter = new DockerSocketWatchdogService.FailureCounter();
        for (var i = 1; i < DockerSocketWatchdogService.FailuresBeforeExit; i++) Assert.That(counter.Record(false), Is.False);
        Assert.That(counter.Record(false), Is.True);
    }

    [Test]
    public void AnAnswerResetsTheCount()
    {
        var counter = new DockerSocketWatchdogService.FailureCounter();
        counter.Record(false); counter.Record(false);
        Assert.That(counter.Record(true), Is.False);
        Assert.That(counter.Record(false), Is.False);
        Assert.That(counter.Record(false), Is.False);
        Assert.That(counter.Record(false), Is.True);
    }
}
