using System.Reflection;
using Automaton.Core.Constants;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class RunnerProtocolCompatibilityTests
{
    [Test]
    public void RunnerAdvertisesTheManagedArtifactProtocolVersion()
    {
        Assert.That(
            HubService.CurrentRunnerApiCompatibilityVersion,
            Is.EqualTo(RunnerApiCompatibilityVersions.Current));
    }

    [Test]
    public void HubServiceContainsTheCanonicalManagedArtifactHandlerContract()
    {
        var handler = typeof(HubService).GetMethod(
            "SynchronizeApplicationManagedArtifacts",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(handler, Is.Not.Null);
        Assert.That(handler!.GetParameters().Single().ParameterType, Is.EqualTo(typeof(SynchronizeApplicationManagedArtifactsRequest)));
        Assert.That(handler.ReturnType, Is.EqualTo(typeof(SynchronizeApplicationManagedArtifactsResult)));
    }
}
