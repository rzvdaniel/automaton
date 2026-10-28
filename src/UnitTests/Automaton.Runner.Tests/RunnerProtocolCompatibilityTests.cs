using System.Reflection;
using Automaton.Core.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerProtocolCompatibilityTests
{
    [Test]
    public void RunnerAdvertisesTheCurrentProtocolVersion()
    {
        Assert.That(
            HubService.CurrentRunnerApiCompatibilityVersion,
            Is.EqualTo(RunnerApiCompatibilityVersions.Current));
    }

    [Test]
    public void HostContainerExecutionIsProtocolFourAndTunnelReconnectCommandsAreProtocolFive()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RunnerApiCompatibilityVersions.HostContainerExecution, Is.EqualTo("4"));
            Assert.That(RunnerApiCompatibilityVersions.RuntimeTunnelReconnectCommands, Is.EqualTo("5"));
            Assert.That(RunnerApiCompatibilityVersions.Current, Is.EqualTo("5"));
            Assert.That(RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed),
                Does.Not.Contain("HostContainerExecution"));
            Assert.That(RunnerCapabilities.DefaultsForPurpose("Infrastructure"),
                Does.Contain("HostContainerExecution"));
        });
    }

    [Test]
    public void ManagedArtifactsUseTheDurableExecutorAndHaveNoHubHandler()
    {
        Assert.That(typeof(HubExecutionHandlerRegistrar).GetMethod("SynchronizeApplicationManagedArtifacts",
            BindingFlags.Instance | BindingFlags.NonPublic), Is.Null);
        Assert.That(typeof(RunnerExecutionRequest).GetProperty("ManagedArtifacts")!.PropertyType,
            Is.EqualTo(typeof(SynchronizeApplicationManagedArtifactsRequest)));
    }
}
