using Automaton.Runner.Console.Connection;
using Microsoft.AspNetCore.SignalR.Client;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public class JitteredReconnectPolicyTests
{
    [Test]
    public void NextRetryDelay_IsBoundedAndIncludesBackoff()
    {
        var policy = new JitteredReconnectPolicy();

        var firstDelay = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 0 });
        var laterDelay = policy.NextRetryDelay(new RetryContext { PreviousRetryCount = 10 });

        Assert.That(firstDelay, Is.Not.Null);
        Assert.That(firstDelay!.Value, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
        Assert.That(firstDelay.Value, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(5)));
        Assert.That(laterDelay, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }
}
