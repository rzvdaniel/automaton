using System.Xml.Linq;
using NUnit.Framework;

namespace Automaton.Architecture.Tests;

[TestFixture]
public sealed class DistributedPlatformArchitectureTests
{
    [Test]
    public void CoreHasNoProjectDependencies()
        => AssertProjectReferencesExactly("Automaton.Core", []);

    [Test]
    public void MessagingHasNoProjectDependencies()
        => AssertProjectReferencesExactly("Automaton.Messaging", []);

    [Test]
    public void InfrastructureDoesNotDependOnHigherLevelOrHostProjects()
        => AssertForbiddenProjectReferences(
            "Automaton.Infrastructure",
            ["Automaton.Api", "Automaton.Runner", "Automaton.Worker", "Automaton.Cli"]);

    [Test]
    public void RunnerDoesNotDependOnControlPlaneOrInfrastructureProjects()
        => AssertForbiddenProjectReferences(
            "Automaton.Runner",
            ["Automaton.Api", "Automaton.Infrastructure", "Automaton.Worker", "Automaton.Cli"]);

    [Test]
    public void WorkerDoesNotDependOnApiRunnerOrCliProjects()
        => AssertForbiddenProjectReferences(
            "Automaton.Worker",
            ["Automaton.Api", "Automaton.Runner", "Automaton.Cli"]);

    [Test]
    public void CliDoesNotDependOnServerRunnerWorkerOrInfrastructureProjects()
        => AssertForbiddenProjectReferences(
            "Automaton.Cli",
            ["Automaton.Api", "Automaton.Infrastructure", "Automaton.Runner", "Automaton.Worker"]);

    [Test]
    public void ApiDoesNotDependOnRunnerWorkerOrCliProjects()
        => AssertForbiddenProjectReferences(
            "Automaton.Api",
            ["Automaton.Runner", "Automaton.Worker", "Automaton.Cli"]);

    private static void AssertProjectReferencesExactly(string projectName, IReadOnlyCollection<string> expected)
    {
        var actual = GetProjectReferences(projectName);
        Assert.That(actual, Is.EqualTo(expected.OrderBy(name => name, StringComparer.Ordinal).ToArray()), projectName);
    }

    private static void AssertForbiddenProjectReferences(
        string projectName,
        IReadOnlyCollection<string> forbidden)
    {
        var actual = GetProjectReferences(projectName);

        Assert.Multiple(() =>
        {
            foreach (var forbiddenProject in forbidden)
            {
                Assert.That(
                    actual,
                    Does.Not.Contain(forbiddenProject),
                    $"{projectName} must not reference {forbiddenProject}.");
            }
        });
    }

    private static string[] GetProjectReferences(string projectName)
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

        return actual!;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
