using Automaton.Api.Models;

namespace Automaton.Api.Tests;

public class PlanCapabilitiesSerializerTests
{
    [Test]
    public void Deserialize_WhenJsonIsEmpty_ReturnsCurrentDefaults()
    {
        var capabilities = PlanCapabilitiesSerializer.Deserialize("");

        Assert.Multiple(() =>
        {
            Assert.That(capabilities.Version, Is.EqualTo(1));
            Assert.That(capabilities.ScheduledActions.Enabled, Is.True);
            Assert.That(capabilities.ScheduledActions.MaximumSchedules, Is.EqualTo(10));
            Assert.That(capabilities.ScheduledActions.MinimumIntervalSeconds, Is.EqualTo(300));
            Assert.That(capabilities.ScheduledActions.MaximumConcurrentExecutions, Is.EqualTo(1));
            Assert.That(capabilities.ScheduledActions.MonthlyExecutionLimit, Is.EqualTo(5000));
        });
    }

    [Test]
    public void Serialize_RoundTripsCurrentCapabilities()
    {
        var source = new PlanCapabilities
        {
            ScheduledActions = new ScheduledActionsCapability { MaximumSchedules = 4 }
        };

        var capabilities = PlanCapabilitiesSerializer.Deserialize(PlanCapabilitiesSerializer.Serialize(source));

        Assert.That(capabilities.ScheduledActions.MaximumSchedules, Is.EqualTo(4));
    }

    [Test]
    public void Deserialize_WhenJsonIsInvalid_ReturnsDefaults()
    {
        var capabilities = PlanCapabilitiesSerializer.Deserialize("{ nope");

        Assert.That(capabilities.ScheduledActions.Enabled, Is.True);
    }

    [TestCase("""{"hostedRunner":{"enabled":true}}""", true)]
    [TestCase("""{"hostedRunner":{"enabled":false}}""", false)]
    [TestCase("""{"scheduledActions":{"enabled":true}}""", false)]
    [TestCase("{ nope", false)]
    public void HasLegacyHostedRunnerEntitlement_OnlyMatchesPreviouslyEnabledPlans(string json, bool expected)
        => Assert.That(PlanCapabilitiesSerializer.HasLegacyHostedRunnerEntitlement(json), Is.EqualTo(expected));
}
