using Automaton.AiGuidance;
using Automaton.Cli.Models;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class AiGuidancePackServiceTests
{
    private readonly List<string> _tempDirectories = [];

    [TearDown]
    public void TearDown()
    {
        foreach (var path in _tempDirectories)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        _tempDirectories.Clear();
    }

    [Test]
    public void Sync_WhenDirectoryIsAnAppRoot_WritesExactlyTheCanonicalFiles()
    {
        var projectRoot = CreateTempDirectory("automaton-cli-guidance-app");
        File.WriteAllText(Path.Combine(projectRoot, "automaton.json"), "{}\n");

        var result = new AiGuidancePackService().Sync(projectRoot, GetExportSources());
        var generatedFiles = result.GeneratedFiles
            .Select(path => Path.GetRelativePath(projectRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.That(result.Mode, Is.EqualTo("app"));
        Assert.That(generatedFiles, Is.EqualTo(new[]
        {
            ".automaton/agents/skills/automaton-app/SKILL.md",
            "AGENTS.md"
        }));
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automaton", "agents", "manifest.json")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(projectRoot, ".automaton", "agents", "references")), Is.False);
    }

    [Test]
    public void Sync_RemovesPreviouslyGeneratedReferencesAndManifest()
    {
        var projectRoot = CreateTempDirectory("automaton-cli-guidance-cleanup");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".automaton", "agents", "references", "llms"));
        File.WriteAllText(Path.Combine(projectRoot, ".automaton", "agents", "references", "llms", "old.md"), "old");
        File.WriteAllText(
            Path.Combine(projectRoot, ".automaton", "agents", "manifest.json"),
            "{\"generator\":{\"name\":\"Automaton.Cli\"}}\n");

        new AiGuidancePackService().Sync(projectRoot, GetExportSources());

        Assert.That(Directory.Exists(Path.Combine(projectRoot, ".automaton", "agents", "references")), Is.False);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automaton", "agents", "manifest.json")), Is.False);
    }

    [Test]
    public void Sync_WithExplicitAdapter_WritesOnlyACanonicalPointer()
    {
        var projectRoot = CreateTempDirectory("automaton-cli-guidance-adapter");

        var result = new AiGuidancePackService().Sync(projectRoot, GetExportSources(), AiGuidanceAdapters.Claude);

        Assert.That(result.GeneratedFiles.Select(path => Path.GetFileName(path)), Does.Contain("CLAUDE.md"));
        var adapter = File.ReadAllText(Path.Combine(projectRoot, "CLAUDE.md"));
        Assert.That(adapter, Does.Contain("AGENTS.md"));
        Assert.That(adapter, Does.Contain(".automaton/agents/skills/automaton-app/SKILL.md"));
        Assert.That(adapter.Length, Is.LessThan(200));
        Assert.That(adapter, Does.Not.Contain("AUTOMATON_PORT"));
    }

    [Test]
    public void Sync_IsDeterministicAndRestoresMissingCanonicalFiles()
    {
        var projectRoot = CreateTempDirectory("automaton-cli-guidance-repeat");
        var service = new AiGuidancePackService();

        service.Sync(projectRoot, GetExportSources());
        var firstAgents = File.ReadAllText(Path.Combine(projectRoot, "AGENTS.md"));
        var firstSkill = File.ReadAllText(Path.Combine(projectRoot, ".automaton", "agents", "skills", "automaton-app", "SKILL.md"));

        File.Delete(Path.Combine(projectRoot, "AGENTS.md"));
        service.Sync(projectRoot, GetExportSources());

        Assert.That(File.ReadAllText(Path.Combine(projectRoot, "AGENTS.md")), Is.EqualTo(firstAgents));
        Assert.That(File.ReadAllText(Path.Combine(projectRoot, ".automaton", "agents", "skills", "automaton-app", "SKILL.md")), Is.EqualTo(firstSkill));

        File.Delete(Path.Combine(projectRoot, ".automaton", "agents", "skills", "automaton-app", "SKILL.md"));
        service.Sync(projectRoot, GetExportSources());

        Assert.That(File.ReadAllText(Path.Combine(projectRoot, ".automaton", "agents", "skills", "automaton-app", "SKILL.md")), Is.EqualTo(firstSkill));

        var firstFiles = Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(projectRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        service.Sync(projectRoot, GetExportSources());
        var secondFiles = Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(projectRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.That(secondFiles, Is.EqualTo(firstFiles));
    }

    [Test]
    public void Sync_WhenDirectoryLooksLikeAppsWorkspace_UsesWorkspaceMode()
    {
        var parentRoot = CreateTempDirectory("automaton-cli-guidance-workspace");
        var workspaceRoot = Path.Combine(parentRoot, "apps");
        Directory.CreateDirectory(workspaceRoot);

        var result = new AiGuidancePackService().Sync(workspaceRoot, GetExportSources());

        Assert.That(result.Mode, Is.EqualTo("workspace"));
    }

    private string CreateTempDirectory(string leaf)
    {
        var root = Path.Combine(Path.GetTempPath(), leaf, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _tempDirectories.Add(root);
        return root;
    }

    private static IReadOnlyList<GuidanceSource> GetExportSources()
    {
        var provider = new EmbeddedAutomatonGuidanceProvider();
        return provider.GetSeedSources()
            .Select(source => AutomatonGuidanceSeedCatalog.Resolve(source.RelativePath))
            .Where(metadata => metadata.Surface == "cli" && metadata.OutputPath is not null)
            .Join(
                provider.GetSeedSources(),
                metadata => metadata.RelativePath,
                source => source.RelativePath,
                (metadata, source) => new GuidanceSource(metadata.OutputPath!, source.Content))
            .ToList();
    }
}
