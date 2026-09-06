using Automaton.Api.Services;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class GenericRuntimeStage2CArchitectureTests
{
    [Test]
    public void RemovedControllerExecutionTypeIsNotLoaded()
    {
        Assert.That(
            Type.GetType("Automaton.Api.Services.AppsControllerExecutionService, Automaton.Api"),
            Is.Null);
    }

    [Test]
    public void ProductionExecutionSourcesDoNotReintroduceControllerBridge()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceFiles = Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "src", "Automaton.Api"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "src", "Automaton.Runner"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "src", "Automaton.Runner", "fastapi_server"), "*.py", SearchOption.AllDirectories))
            .Where(path => !path.Contains("Migrations", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.Combine("Models", "AppControllerModels.cs"), StringComparison.OrdinalIgnoreCase));

        var forbidden = new[]
        {
            "AppsControllerExecutionService",
            "UploadControllerScript",
            "controller_runner.py",
            "/api/controller",
            "handle(action, inputs)"
        };

        foreach (var path in sourceFiles)
        {
            var contents = File.ReadAllText(path);
            foreach (var term in forbidden)
                Assert.That(contents, Does.Not.Contain(term), $"{term} reintroduced in {path}");
        }
    }

    [Test]
    public void LegacyRuntimeMarkerIsAbsentFromRuntimeDecisionSources()
    {
        var repositoryRoot = FindRepositoryRoot();
        var decisionRoots = new[]
        {
            Path.Combine(repositoryRoot, "src", "Automaton.Api", "Services"),
            Path.Combine(repositoryRoot, "src", "Automaton.Api", "Controllers"),
            Path.Combine(repositoryRoot, "src", "Automaton.Api", "Hubs"),
            Path.Combine(repositoryRoot, "src", "Automaton.Runner", "Services")
        };

        foreach (var path in decisionRoots.SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)))
            Assert.That(File.ReadAllText(path), Does.Not.Contain("LegacyControllerRuntimeDeployed"), path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
