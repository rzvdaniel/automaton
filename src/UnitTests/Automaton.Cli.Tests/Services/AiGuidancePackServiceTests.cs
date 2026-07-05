using Automaton.Cli.Services;
using Automaton.Cli.Models;
using Automaton.AiGuidance;
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

        var result = new AiGuidancePackService().Sync(projectRoot, GetReferenceSources());

        Assert.That(result.Mode, Is.EqualTo("app"));
        Assert.That(File.Exists(Path.Combine(projectRoot, "AGENTS.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, "CLAUDE.md")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(projectRoot, ".github")), Is.False);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automaton", "agents", "instructions.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automaton", "agents", "skills", "automaton-app", "SKILL.md")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automaton", "agents", "manifest.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".automaton", "agents", "references", "llms.txt")), Is.True);
        Assert.That(File.Exists(result.ManifestPath), Is.True);

        using var manifest = JsonDocument.Parse(File.ReadAllText(result.ManifestPath));
        Assert.That(manifest.RootElement.GetProperty("mode").GetString(), Is.EqualTo("app"));
        Assert.That(manifest.RootElement.GetProperty("sources").GetArrayLength(), Is.GreaterThanOrEqualTo(6));
        Assert.That(manifest.RootElement.GetProperty("entrypoints").GetProperty("references").GetString(), Is.EqualTo(".automaton/agents/references/llms.txt"));
        Assert.That(manifest.RootElement.GetProperty("adapters").GetArrayLength(), Is.EqualTo(1));

        var agentsContent = File.ReadAllText(Path.Combine(projectRoot, "AGENTS.md"));
        Assert.That(agentsContent, Does.Contain(".automaton/agents/references/llms.txt"));
    }

    [Test]
    public void Sync_WhenDirectoryIsAnAppRoot_GeneratesThinWrappersAndReferenceGuardrails()
    {
        var projectRoot = CreateTempDirectory("automaton-cli-guidance-guardrails");
        File.WriteAllText(Path.Combine(projectRoot, "automaton.json"), "{}\n");

        new AiGuidancePackService().Sync(projectRoot, GetReferenceSources());

        var agentsContent = File.ReadAllText(Path.Combine(projectRoot, "AGENTS.md"));
        var instructionsContent = File.ReadAllText(Path.Combine(projectRoot, ".automaton", "agents", "instructions.md"));
        var skillContent = File.ReadAllText(Path.Combine(projectRoot, ".automaton", "agents", "skills", "automaton-app", "SKILL.md"));
        var llmsContent = File.ReadAllText(Path.Combine(projectRoot, ".automaton", "agents", "references", "llms.txt"));

        Assert.That(agentsContent, Does.Contain("## STOP"));
        Assert.That(agentsContent, Does.Contain("Before creating files:"));
        Assert.That(agentsContent, Does.Contain("Check for `automaton.json`"));
        Assert.That(agentsContent, Does.Contain("Do not create a custom project structure first"));
        Assert.That(agentsContent, Does.Contain("This is an Automaton-managed local project"));
        Assert.That(agentsContent, Does.Contain("Do not invent a generic project structure before making that decision"));
        Assert.That(agentsContent, Does.Contain("automaton validate"));
        Assert.That(agentsContent, Does.Contain("Do not create FastAPI, Flask, Express, or custom HTTP servers inside an Automaton backend"));
        Assert.That(agentsContent, Does.Contain("Do not treat this file as the full policy"));
        Assert.That(agentsContent, Does.Contain("If it does not exist and the user wants a new app here, run `automaton new"));
        Assert.That(agentsContent, Does.Contain("Do not run `automaton publish` unless the user explicitly requests publishing"));
        Assert.That(agentsContent, Does.Contain("Do not leave local dev logs, test databases, temp files, or generated runtime artifacts inside the app folder before deploy"));
        Assert.That(instructionsContent, Does.Contain("Do not invent an Automaton project structure from scratch before checking local files"));
        Assert.That(instructionsContent, Does.Contain("The canonical Automaton rules live in `.automaton/agents/references/`"));
        Assert.That(instructionsContent, Does.Contain("Before making app bootstrap decisions, check whether `automaton.json` exists"));
        Assert.That(instructionsContent, Does.Contain("Frontend source normally lives in `frontend/`"));
        Assert.That(instructionsContent, Does.Contain("Treat `automaton deploy` success as the completion gate for app work"));
        Assert.That(instructionsContent, Does.Contain("`automaton deploy` is allowed for build verification and testing"));
        Assert.That(instructionsContent, Does.Contain("Do not leave local dev logs, test databases, temp files, or generated runtime artifacts inside the app folder before deploy"));
        Assert.That(skillContent, Does.Contain("Check whether `automaton.json` exists in the project root"));
        Assert.That(skillContent, Does.Contain("Default to frontend-only unless backend is explicitly needed"));
        Assert.That(skillContent, Does.Contain("Use `automaton validate` when the local contract or deploy shape is unclear"));
        Assert.That(skillContent, Does.Contain("Never create FastAPI, Flask, Express, aiohttp, uvicorn entrypoints, or custom HTTP listeners under `backend/`"));
        Assert.That(skillContent, Does.Not.Contain("python -m py_compile"));
        Assert.That(llmsContent, Does.Contain("If it does not exist and this is a new app, run `automaton new"));
        Assert.That(llmsContent, Does.Contain("Do not create a backend for convenience"));
        Assert.That(llmsContent, Does.Contain("Do not claim the app is done until `automaton deploy` succeeds"));
        Assert.That(llmsContent, Does.Contain("Publishing is a user decision and requires explicit approval"));
        Assert.That(llmsContent, Does.Contain("Do not leave local dev logs, test databases, temp files, or generated runtime artifacts inside the app folder before deploy"));
    }

    [Test]
    public void Sync_WhenLegacyGeneratedAdaptersExist_RemovesOnlyGeneratedAdapters()
    {
        var projectRoot = CreateTempDirectory("automaton-cli-guidance-legacy");
        File.WriteAllText(Path.Combine(projectRoot, "CLAUDE.md"), "This file is generated by `automaton`. Do not edit it manually.");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".github"));
        File.WriteAllText(Path.Combine(projectRoot, ".github", "copilot-instructions.md"), "This file is generated by `automaton`. Do not edit it manually.");
        Directory.CreateDirectory(Path.Combine(projectRoot, ".automaton", "ai-guidance", "llms"));
        File.WriteAllText(Path.Combine(projectRoot, ".automaton", "ai-guidance", "llms.txt"), "legacy llms");
        File.WriteAllText(Path.Combine(projectRoot, ".automaton", "ai-guidance", "llms", "automaton-cli-install.md"), "legacy install");
        File.WriteAllText(Path.Combine(projectRoot, ".automaton", "ai-guidance", "manifest.json"), """{"generator":{"name":"Automaton.Cli"}}""");

        new AiGuidancePackService().Sync(projectRoot, GetReferenceSources());

        Assert.That(File.Exists(Path.Combine(projectRoot, "CLAUDE.md")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(projectRoot, ".github")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(projectRoot, ".automaton", "ai-guidance")), Is.False);
    }

    [Test]
    public void Sync_WhenClaudeAdapterRequested_GeneratesClaudeAdapterOnly()
    {
        var projectRoot = CreateTempDirectory("automaton-cli-guidance-claude");

        new AiGuidancePackService().Sync(projectRoot, GetReferenceSources(), AiGuidanceAdapters.Claude);

        var claudePath = Path.Combine(projectRoot, "CLAUDE.md");
        Assert.That(File.Exists(claudePath), Is.False);
        Assert.That(Directory.Exists(Path.Combine(projectRoot, ".github")), Is.False);
    }

    [Test]
    public void Sync_WhenAllAdaptersRequested_GeneratesAllOptionalAdapters()
    {
        var projectRoot = CreateTempDirectory("automaton-cli-guidance-all");

        new AiGuidancePackService().Sync(projectRoot, GetReferenceSources(), AiGuidanceAdapters.All);

        Assert.That(File.Exists(Path.Combine(projectRoot, "CLAUDE.md")), Is.False);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".github", "copilot-instructions.md")), Is.False);
        Assert.That(File.Exists(Path.Combine(projectRoot, ".github", "skills", "automaton-app", "SKILL.md")), Is.False);
    }

    [Test]
    public void Sync_WhenDirectoryLooksLikeAppsWorkspace_GeneratesWorkspacePack()
    {
        var parentRoot = CreateTempDirectory("automaton-cli-guidance-workspace");
        var workspaceRoot = Path.Combine(parentRoot, "apps");
        Directory.CreateDirectory(workspaceRoot);

        var result = new AiGuidancePackService().Sync(workspaceRoot, GetReferenceSources());

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

    private static IReadOnlyList<GuidanceSource> GetReferenceSources() =>
        BuildExportSources();

    private static IReadOnlyList<GuidanceSource> BuildExportSources()
    {
        var provider = new EmbeddedAutomatonGuidanceProvider();
        var sources = new List<GuidanceSource>
        {
            new("AGENTS.md", provider.GetTemplate("AGENTS.md")),
            new(".automaton/agents/instructions.md", provider.GetTemplate("instructions.md")),
            new(".automaton/agents/skills/automaton-app/SKILL.md", provider.GetTemplate("SKILL.md")),
            new(".automaton/agents/references/llms.txt", provider.GetReference("llms.txt")),
            new(".automaton/agents/references/llms-full.txt", provider.GetReference("llms-full.txt")),
        };

        foreach (var source in provider.GetReferenceSources().Where(item => item.RelativePath.StartsWith("llms/", StringComparison.Ordinal)))
        {
            var fileName = Path.GetFileName(source.RelativePath);
            sources.Add(new GuidanceSource($".automaton/agents/references/llms/{fileName}", source.Content));
        }

        return sources;
    }
}
