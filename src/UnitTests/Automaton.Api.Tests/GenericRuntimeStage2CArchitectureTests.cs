using Automaton.Api.Services;
using Automaton.Api.Models;
using Automaton.Api.Entities;

namespace Automaton.Api.Tests;

[TestFixture]
public sealed class GenericRuntimeStage3ArchitectureTests
{
    [Test]
    public void RemovedControllerExecutionTypeIsNotLoaded()
    {
        Assert.That(
            Type.GetType("Automaton.Api.Services.AppsControllerExecutionService, Automaton.Api"),
            Is.Null);
    }

    [Test]
    public void ProductionExecutionSourcesDoNotReintroduceHistoricalApplicationExecution()
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

    [Test]
    public void ActiveStorageAndDeploymentModelsExposeOnlyGenericRuntimeState()
    {
        Assert.That(typeof(AppStorageMetadata).GetProperty("Frontend"), Is.Null);
        Assert.That(typeof(AppStorageMetadata).GetProperty("Backend"), Is.Null);
        Assert.That(typeof(AppModel).GetProperty("Frontend"), Is.Null);
        Assert.That(typeof(AppModel).GetProperty("Backend"), Is.Null);
        Assert.That(typeof(AppDeploymentTarget).GetProperty("LegacyControllerRuntimeDeployed"), Is.Null);
        Assert.That(typeof(AppDeploymentTarget).GetProperty(nameof(AppDeploymentTarget.GenericRuntimeDeployed)), Is.Not.Null);
    }

    [Test]
    public void ActiveProductSurfacesDoNotReintroduceSplitRuntimeSemantics()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activeSurfaces = new[]
        {
            Path.Combine(repositoryRoot, "src", "Automaton.Api", "Services", "AppsService.cs"),
            Path.Combine(repositoryRoot, "src", "Automaton.Api", "Controllers", "PublicMarketplaceController.cs"),
            Path.Combine(repositoryRoot, "src", "automaton-studio-vue", "src", "features", "apps", "stores", "app-editor.store.ts"),
            Path.Combine(repositoryRoot, "src", "automaton-studio-vue", "src", "features", "apps", "views", "AppForkView.vue"),
            Path.Combine(repositoryRoot, "src", "automaton-vue", "src", "views", "AppMarketplaceDetailView.vue")
        };
        var forbidden = new[]
        {
            "source/frontend", "source/backend", "live/frontend", "live/backend",
            "CanDeployFrontendFromStudio", "CanDeployBackendFromStudio",
            "DeployFrontend", "DeployBackend", "LegacyControllerRuntimeDeployed"
        };

        foreach (var path in activeSurfaces)
        {
            var contents = File.ReadAllText(path);
            foreach (var term in forbidden)
                Assert.That(contents, Does.Not.Contain(term), $"{term} reintroduced in {path}");
        }
    }

    [Test]
    public void RemixCreationCannotPublishOrAdvertiseADeploymentBeforeGenericDeployment()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appsService = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Automaton.Api", "Services", "AppsService.cs"));
        var forkView = File.ReadAllText(Path.Combine(repositoryRoot, "src", "automaton-studio-vue", "src", "features", "apps", "views", "AppForkView.vue"));

        Assert.That(appsService, Does.Not.Contain("request.PublishImmediately"));
        Assert.That(appsService, Does.Contain("IsPublished = false"));
        Assert.That(appsService, Does.Contain("PublicUrl = null"));
        Assert.That(forkView, Does.Not.Contain("Public immediately"));
        Assert.That(forkView, Does.Not.Contain("To run your remix, you need your own Runner"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
