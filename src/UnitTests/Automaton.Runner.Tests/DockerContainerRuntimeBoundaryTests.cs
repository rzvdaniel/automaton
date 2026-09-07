using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

[TestFixture]
public sealed class DockerContainerRuntimeBoundaryTests
{
    [Test]
    public void HostPathTranslationUsesTheActualInfrastructureRunnerMountShape()
    {
        var translator = new RunnerManagedHostPathTranslator(
            "/automaton/workspace",
            "/var/lib/automaton/infrastructure-runner/workflows");

        Assert.That(
            translator.Translate("/automaton/workspace/apps/x/runtime"),
            Is.EqualTo("/var/lib/automaton/infrastructure-runner/workflows/apps/x/runtime"));
    }

    [Test]
    public void HostPathTranslationRejectsPathsOutsideTheManagedWorkspace()
    {
        var translator = new RunnerManagedHostPathTranslator(
            "/automaton/workspace",
            "/var/lib/automaton/infrastructure-runner/workflows");

        Assert.Throws<UnauthorizedAccessException>(() => translator.Translate("/etc/passwd"));
    }
}
