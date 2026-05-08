using Automaton.Cli.Services;
using NUnit.Framework;
using System.Text.Json;

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
    public void Sync_WhenDirectoryIsAnAppRoot_GeneratesAgentPackAndManifest()
    {
        var projectRoot = CreateTempDirectory("automaton-cli-guidance-app");
        File.WriteAllText(Path.Combine(projectRoot, "automaton.json"), "{}\n");

        var result = new AiGuidancePackService().Sync(projectRoot);

        Assert.That(result.Mode, Is.EqualTo("app"));
        Assert.That(File.Exists(Path.Combine(projectRoot, "AGENTS.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".github", "copilot-instructions.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".github", "skills", "automaton-app", "SKILL.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automaton", "ai-guidance", "llms.txt")), Is.True);
        Assert.That(File.Exists(result.ManifestPath), Is.True);

        using var manifest = JsonDocument.Parse(File.ReadAllText(result.ManifestPath));
        Assert.That(manifest.RootElement.GetProperty("mode").GetString(), Is.EqualTo("app"));
        Assert.That(manifest.RootElement.GetProperty("sources").GetArrayLength(), Is.GreaterThanOrEqualTo(6));
    }

    [Test]
    public void Sync_WhenDirectoryLooksLikeAppsWorkspace_GeneratesWorkspacePack()
    {
        var parentRoot = CreateTempDirectory("automaton-cli-guidance-workspace");
        var workspaceRoot = Path.Combine(parentRoot, "apps");
        Directory.CreateDirectory(workspaceRoot);

        var result = new AiGuidancePackService().Sync(workspaceRoot);

        Assert.That(result.Mode, Is.EqualTo("workspace"));
        var agentsContent = File.ReadAllText(Path.Combine(workspaceRoot, "AGENTS.md"));
        Assert.That(agentsContent, Does.Contain("apps workspace"));
    }

    private string CreateTempDirectory(string leaf)
    {
        var root = Path.Combine(Path.GetTempPath(), leaf, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _tempDirectories.Add(root);
        return root;
    }
}