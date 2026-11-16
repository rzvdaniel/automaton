using Automaton.Api.Services;
using Automaton.Core.Models;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class RunnerTunnelRegistryTests
{
    private static TunnelConnectionStream NewStream() => new(new MemoryStream());

    [Test]
    public void Add_RefusesConnectionsBeyondTheCapAtomically()
    {
        var registry = new RunnerTunnelRegistry();
        var runnerId = Guid.NewGuid();
        var registrations = Enumerable.Range(0, 20).AsParallel()
            .Select(i => registry.Add(runnerId, NewStream(), out _, maxConnections: 3))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(registrations.Count(r => r is not null), Is.EqualTo(3));
            Assert.That(registry.ConnectionCount(runnerId), Is.EqualTo(3));
        });
    }

    [Test]
    public void TryAcquire_PicksTheLeastBusyConnectionAndAClosedOneIsNeverPicked()
    {
        var registry = new RunnerTunnelRegistry();
        var runnerId = Guid.NewGuid();
        var closing = NewStream();
        registry.Add(runnerId, NewStream(), out _);
        registry.Add(runnerId, closing, out _);

        using var first = registry.TryAcquire(runnerId);
        using var second = registry.TryAcquire(runnerId);
        Assert.That(first!.Invoker, Is.Not.SameAs(second!.Invoker), "the second request should use the idle connection");

        closing.Dispose();
        Assert.That(registry.ConnectionCount(runnerId), Is.EqualTo(1));
    }

    [Test]
    public void DisposingTheLastRegistration_ForgetsTheRunner()
    {
        var registry = new RunnerTunnelRegistry();
        var runnerId = Guid.NewGuid();
        var registration = registry.Add(runnerId, NewStream(), out _)!;

        registration.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(registry.IsConnected(runnerId), Is.False);
            Assert.That(registry.TryAcquire(runnerId), Is.Null);
        });
    }
}
