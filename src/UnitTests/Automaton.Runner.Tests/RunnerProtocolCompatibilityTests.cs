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
    public void HostContainerExecutionIsProtocolFourAndInfrastructureOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RunnerApiCompatibilityVersions.HostContainerExecution, Is.EqualTo("4"));
            Assert.That(RunnerApiCompatibilityVersions.Current, Is.EqualTo("4"));
            Assert.That(RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Managed),
                Does.Not.Contain(RunnerCapabilities.HostContainerExecution));
            Assert.That(RunnerCapabilities.DefaultsForPurpose(RunnerPurposes.Infrastructure),
                Does.Contain(RunnerCapabilities.HostContainerExecution));
        });
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
