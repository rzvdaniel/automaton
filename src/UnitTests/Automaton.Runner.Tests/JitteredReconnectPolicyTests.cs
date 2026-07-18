using Automaton.Runner.Console.Connection;
using Microsoft.AspNetCore.SignalR.Client;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class JitteredReconnectPolicyTests
{
    [TestCase(0.0, 1.0)]
    [TestCase(0.5, 3.5)]
    [TestCase(0.999, 5.995)]
    public void FirstRetry_UsesExpectedBackoffAndJitter(double jitter, double expectedSeconds)
    {
        var policy = new JitteredReconnectPolicy(() => jitter);

        var delay = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 0 });

        Assert.That(delay, Is.Not.Null);
        Assert.That(delay!.Value.TotalSeconds, Is.EqualTo(expectedSeconds).Within(0.001));
    }

    [Test]
    public void LaterRetries_AreCappedAtThirtySeconds()
    {
        var policy = new JitteredReconnectPolicy(() => 0.999);

        var delay = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 10 });

        Assert.That(delay, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }
}
