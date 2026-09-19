using System.Xml.Linq;
using NUnit.Framework;

namespace Automaton.Architecture.Tests;

[TestFixture]
public sealed class DistributedPlatformArchitectureTests
{
    [Test]
    public void CoreHasNoProjectDependencies()
        => AssertProjectReferences("Automaton.Core", []);

    [Test]
    public void MessagingHasNoProjectDependencies()
        => AssertProjectReferences("Automaton.Messaging", []);

    [Test]
    public void InfrastructureDependsOnlyOnCore()
        => AssertProjectReferences("Automaton.Infrastructure", ["Automaton.Core"]);

    [Test]
    public void RunnerDependsOnlyOnSharedContractsAndSteps()
        => AssertProjectReferences(
            "Automaton.Runner",
            ["Automaton.Core", "Automaton.Messaging", "Automaton.Steps"]);

    [Test]
    public void WorkerDependsOnMessagingAndInfrastructureButNotApiOrRunner()
        => AssertProjectReferences(
            "Automaton.Worker",
            ["Automaton.Messaging", "Automaton.Infrastructure", "Automaton.Identity"]);

    [Test]
    public void CliDependsOnReusableContractsRatherThanServerOrInfrastructureImplementations()
        => AssertProjectReferences("Automaton.Cli", ["Automaton.Core", "Automaton.AiGuidance"]);

    private static void AssertProjectReferences(string projectName, IReadOnlyCollection<string> expected)
    {
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            projectName,
            $"{projectName}.csproj");

        var actual = XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")?.Value))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(actual, Is.EqualTo(expected.OrderBy(name => name, StringComparer.Ordinal).ToArray()), projectPath);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
