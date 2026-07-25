using Automaton.Api.Models;

namespace Automaton.Api.Tests;

public class PlanCapabilitiesSerializerTests
{
    [Test]
    public void Deserialize_WhenJsonIsEmpty_ReturnsDefaultCapabilities()
    {
        var capabilities = PlanCapabilitiesSerializer.Deserialize("");

        Assert.Multiple(() =>
        {
            Assert.That(capabilities.Version, Is.EqualTo(1));
            Assert.That(capabilities.HostedRunner.Enabled, Is.False);
            Assert.That(capabilities.HostedRunner.Visibility, Is.EqualTo(HostedRunnerVisibility.Disabled));
            Assert.That(capabilities.ScheduledActions.Enabled, Is.True);
            Assert.That(capabilities.ScheduledActions.MaximumSchedules, Is.EqualTo(10));
            Assert.That(capabilities.ScheduledActions.MinimumIntervalSeconds, Is.EqualTo(300));
            Assert.That(capabilities.ScheduledActions.MaximumConcurrentExecutions, Is.EqualTo(1));
            Assert.That(capabilities.ScheduledActions.MonthlyExecutionLimit, Is.EqualTo(5000));
        });
    }

    [Test]
    public void Serialize_RoundTripsHostedRunnerCapabilities()
    {
        var source = new PlanCapabilities
        {
            HostedRunner = new HostedRunnerCapability
            {
                Enabled = true,
                Visibility = HostedRunnerVisibility.TestUsersOnly
            }
        };

        var json = PlanCapabilitiesSerializer.Serialize(source);
        var capabilities = PlanCapabilitiesSerializer.Deserialize(json);

        Assert.Multiple(() =>
        {
            Assert.That(capabilities.HostedRunner.Enabled, Is.True);
            Assert.That(capabilities.HostedRunner.Visibility, Is.EqualTo(HostedRunnerVisibility.TestUsersOnly));
        });
    }

    [Test]
    public void Deserialize_WhenVisibilityIsUnknown_NormalizesToDisabled()
    {
        const string json = """
        {
          "version": 1,
          "hostedRunner": {
            "enabled": true,
            "cpuCores": 1,
            "memoryMb": 1024,
            "diskMb": 4096,
            "maxApps": 5,
            "visibility": "friendsOnly"
          }
        }
        """;

        var capabilities = PlanCapabilitiesSerializer.Deserialize(json);

        Assert.That(capabilities.HostedRunner.Visibility, Is.EqualTo(HostedRunnerVisibility.Disabled));
    }

    [Test]
    public void Deserialize_WhenJsonIsInvalid_ReturnsDefaultCapabilities()
    {
        var capabilities = PlanCapabilitiesSerializer.Deserialize("{ nope");

        Assert.That(capabilities.HostedRunner.Visibility, Is.EqualTo(HostedRunnerVisibility.Disabled));
    }

    [TestCase(HostedRunnerVisibility.Public, false, true)]
    [TestCase(HostedRunnerVisibility.Public, true, true)]
    [TestCase(HostedRunnerVisibility.TestUsersOnly, false, false)]
    [TestCase(HostedRunnerVisibility.TestUsersOnly, true, true)]
    [TestCase(HostedRunnerVisibility.Disabled, false, false)]
    public void HostedRunnerPlanVisibility_RespectsCatalogVisibility(
        string visibility,
        bool canAccessHostedRunnerTestPlans,
        bool expected)
    {
        var capabilities = new PlanCapabilities
        {
            HostedRunner = new HostedRunnerCapability
            {
                Enabled = true,
                Visibility = visibility
            }
        };

        var visible = HostedRunnerPlanVisibility.IsVisibleInCatalog(
            capabilities,
            canAccessHostedRunnerTestPlans);

        Assert.That(visible, Is.EqualTo(expected));
    }
}
