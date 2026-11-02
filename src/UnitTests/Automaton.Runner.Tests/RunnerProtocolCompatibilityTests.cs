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
    public void ManagedArtifactsUseTheDurableExecutorAndHaveNoHubHandler()
    {
        Assert.That(typeof(HubExecutionHandlerRegistrar).GetMethod("SynchronizeApplicationManagedArtifacts",
            BindingFlags.Instance | BindingFlags.NonPublic), Is.Null);
        Assert.That(typeof(RunnerExecutionRequest).GetProperty("ManagedArtifacts")!.PropertyType,
            Is.EqualTo(typeof(SynchronizeApplicationManagedArtifactsRequest)));
    }
}
