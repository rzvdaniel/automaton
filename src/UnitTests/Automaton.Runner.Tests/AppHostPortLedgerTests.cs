using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

public sealed class AppHostPortLedgerTests
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "ledger-" + Guid.NewGuid().ToString("N"));
    private AppHostPortLedger Create(int start = 18000, int end = 18010) => new(Path.Combine(dir, "ports.json"), start, end);
    private static readonly Func<int, bool> Free = _ => true;

    [Test]
    public void AnAppKeepsItsPortAcrossRestartsAndNewLedgerInstances()
    {
        var app = Guid.NewGuid(); var target = Guid.NewGuid();
        var first = Create().Reserve(app, target, new HashSet<int>(), Free);
        Assert.That(Create().Reserve(app, target, new HashSet<int>(), Free), Is.EqualTo(first));
    }

    [Test]
    public void TwoAppsNeverShareAPortEvenWhenOneContainerIsGone()
    {
        var ledger = Create();
        var a = ledger.Reserve(Guid.NewGuid(), Guid.NewGuid(), new HashSet<int>(), Free);
        var appB = ledger.Reserve(Guid.NewGuid(), Guid.NewGuid(), new HashSet<int>(), Free);
        Assert.That(appB, Is.Not.EqualTo(a));
    }

    [Test]
    public void ACandidateGetsTheAppsOtherPortWhileTheRunningContainerKeepsItsOwn()
    {
        var ledger = Create(); var app = Guid.NewGuid(); var target = Guid.NewGuid();
        var live = ledger.Reserve(app, target, new HashSet<int>(), Free);
        var candidate = ledger.Reserve(app, target, new HashSet<int> { live }, Free);
        Assert.That(candidate, Is.Not.EqualTo(live));
        Assert.That(ledger.Reserve(app, target, new HashSet<int> { candidate }, Free), Is.EqualTo(live));
        Assert.Throws<InvalidOperationException>(() => ledger.Reserve(app, target, new HashSet<int> { live, candidate }, Free));
    }

    [Test]
    public void AReleasedPortCanGoToAnotherApp()
    {
        var ledger = Create(18000, 18000); var app = Guid.NewGuid(); var target = Guid.NewGuid();
        var port = ledger.Reserve(app, target, new HashSet<int>(), Free);
        Assert.Throws<InvalidOperationException>(() => ledger.Reserve(Guid.NewGuid(), Guid.NewGuid(), new HashSet<int>(), Free));
        ledger.Release(app, target);
        Assert.That(ledger.Reserve(Guid.NewGuid(), Guid.NewGuid(), new HashSet<int>(), Free), Is.EqualTo(port));
    }

    [Test]
    public void ParsesPublishedHostPortsFromDockerPs()
        => Assert.That(AppHostPortLedger.ParsePublishedPorts("127.0.0.1:18003->3000/tcp"), Is.EquivalentTo(new[] { 18003 }));

    [TearDown]
    public void Dispose() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
