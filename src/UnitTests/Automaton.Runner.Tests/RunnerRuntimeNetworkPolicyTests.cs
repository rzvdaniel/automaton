using System.Net.Sockets;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

public sealed class RunnerRuntimeNetworkPolicyTests
{
    [Test]
    public async Task ConnectionRefusedAtExpiredProbeDeadlineRemainsAReadinessFailure()
    {
        using var deadline = new CancellationTokenSource();
        await deadline.CancelAsync();
        var error = new SocketException((int)SocketError.ConnectionRefused);
        Assert.That(deadline.IsCancellationRequested, Is.True);
        Assert.That(RunnerRuntimeNetworkPolicy.IsReadinessProbeFailure(error, CancellationToken.None), Is.True);
        Assert.That(RunnerRuntimeNetworkPolicy.IsReadinessProbeFailure(new TaskCanceledException(), CancellationToken.None), Is.True);
    }

    [Test]
    public async Task CallerCancellationIsNeverConvertedToReadinessRetry()
    {
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        Assert.That(RunnerRuntimeNetworkPolicy.IsReadinessProbeFailure(
            new SocketException((int)SocketError.ConnectionRefused), caller.Token), Is.False);
        Assert.That(RunnerRuntimeNetworkPolicy.IsReadinessProbeFailure(new TaskCanceledException(), caller.Token), Is.False);
        Assert.That(RunnerRuntimeNetworkPolicy.IsReadinessProbeFailure(new InvalidOperationException(), CancellationToken.None), Is.False);
    }

    [Test]
    public void ReadinessFailsAtOnceWhenTheContainerHasExited_NotAfterTheStartupTimeout()
    {
        var policy = new RunnerRuntimeNetworkPolicy(new RunnerRuntimeRegistry());
        var started = DateTime.UtcNow;
        var error = Assert.ThrowsAsync<InvalidOperationException>(() => policy.WaitForReadinessAsync(
            1, new Automaton.Core.Models.ApplicationHealthSpec { StartupTimeoutSeconds = 120 }, CancellationToken.None,
            _ => Task.FromResult(false)));
        Assert.That(error!.Message, Does.Contain("exited"));
        Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(20)));
    }
}
