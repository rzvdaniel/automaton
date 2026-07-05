namespace Automaton.AiGuidance;

public static class AutomatonGuidanceSeedCatalog
{
    private static readonly IReadOnlyDictionary<string, AutomatonGuidanceSeedMetadata> MetadataByRelativePath =
        new Dictionary<string, AutomatonGuidanceSeedMetadata>(StringComparer.Ordinal)
        {
            ["llms/automaton-app-contract-compact.md"] = new("llms/automaton-app-contract-compact.md", "automaton-app-contract-compact", "Automaton App Contract (Compact)", "shared", "BasePrompt", "Compact always-on Automaton app contract for Studio editing.", """["contract","structure","studio"]""", true, false, 0, null),
            ["llms/automaton-studio-editor.md"] = new("llms/automaton-studio-editor.md", "automaton-studio-editor", "Automaton Studio Editor", "studio", "Guidance", "Studio editor behavior, file editing rules, and verification expectations.", """["editor","files","preview","studio"]""", true, true, 100, ".automaton/agents/references/llms/automaton-studio-editor.md"),
            ["llms/automaton-backend-controllers.md"] = new("llms/automaton-backend-controllers.md", "automaton-backend-controllers", "Automaton Backend Controllers", "shared", "Guidance", "Python controller rules, response envelope, and backend structure guidance.", """["backend","controllers","api","python"]""", false, true, 200, ".automaton/agents/references/llms/automaton-backend-controllers.md"),
            ["llms/automaton-media-assets.md"] = new("llms/automaton-media-assets.md", "automaton-media-assets", "Automaton Media and Static Asset Rules", "shared", "Guidance", "Bundled assets versus runtime media, uploads, and public/static asset handling.", """["assets","media","uploads","images"]""", false, true, 200, ".automaton/agents/references/llms/automaton-media-assets.md"),
            ["llms/automaton-frontend-structure.md"] = new("llms/automaton-frontend-structure.md", "automaton-frontend-structure", "Automaton Frontend Structure", "shared", "Guidance", "Frontend structure, relative URLs, PWA rules, and installable app patterns.", """["frontend","pwa","structure","vite"]""", false, true, 200, ".automaton/agents/references/llms/automaton-frontend-structure.md"),
            ["llms/automaton-starter-prompts.md"] = new("llms/automaton-starter-prompts.md", "automaton-starter-prompts", "Automaton Starter Prompts", "cli", "Guidance", "Starter app prompts and initial app creation guidance.", """["starter","scaffold","examples","cli"]""", false, true, 200, ".automaton/agents/references/llms/automaton-starter-prompts.md"),
            ["llms/automaton-cli-deploy.md"] = new("llms/automaton-cli-deploy.md", "automaton-cli-deploy", "Automaton CLI Deploy", "cli", "Guidance", "Deploy and runtime troubleshooting guidance for Automaton apps.", """["deploy","publish","runtime","cli"]""", false, true, 200, ".automaton/agents/references/llms/automaton-cli-deploy.md"),
            ["llms/automaton-cli-install.md"] = new("llms/automaton-cli-install.md", "automaton-cli-install", "Automaton CLI Install", "cli", "Guidance", "Automaton CLI installation and bootstrap guidance.", """["install","setup","bootstrap","cli"]""", false, true, 200, ".automaton/agents/references/llms/automaton-cli-install.md"),
            ["llms/automaton-examples.md"] = new("llms/automaton-examples.md", "automaton-examples", "Automaton Examples", "shared", "Guidance", "Longer examples and reference app patterns.", """["examples","patterns","reference"]""", false, true, 200, ".automaton/agents/references/llms/automaton-examples.md"),
            ["llms/automaton-mcp.md"] = new("llms/automaton-mcp.md", "automaton-mcp", "Automaton MCP", "mcp", "Guidance", "Guidance for MCP-related workflows and integrations.", """["mcp","protocol","tools"]""", false, true, 200, ".automaton/agents/references/llms/automaton-mcp.md"),
            ["AGENTS.md"] = new("AGENTS.md", "cli-agents", "CLI AGENTS", "cli", "Guidance", "Root local agent wrapper for Automaton projects.", """["wrapper","cli","agents"]""", false, false, 300, "AGENTS.md"),
            ["instructions.md"] = new("instructions.md", "cli-instructions", "CLI Instructions", "cli", "Guidance", "Canonical local instructions wrapper for Automaton agents.", """["wrapper","cli","instructions"]""", false, false, 310, ".automaton/agents/instructions.md"),
            ["SKILL.md"] = new("SKILL.md", "cli-skill-automaton-app", "CLI Automaton App Skill", "cli", "Guidance", "Local skill entrypoint for Automaton app workflows.", """["wrapper","cli","skill"]""", false, false, 320, ".automaton/agents/skills/automaton-app/SKILL.md")
        };

    public static IReadOnlyCollection<AutomatonGuidanceSeedMetadata> All => MetadataByRelativePath.Values.ToArray();

    public static AutomatonGuidanceSeedMetadata ResolveByRelativePath(string relativePath) =>
        MetadataByRelativePath.TryGetValue(relativePath, out var metadata)
            ? metadata
            : new AutomatonGuidanceSeedMetadata(
                relativePath,
                relativePath.Replace("llms/", string.Empty, StringComparison.Ordinal).Replace(".md", string.Empty, StringComparison.Ordinal),
                relativePath,
                "shared",
                "Guidance",
                "Focused Automaton guidance reference.",
                """["reference"]""",
                false,
                true,
                200,
                relativePath.StartsWith("llms/", StringComparison.Ordinal)
                    ? $".automaton/agents/references/{relativePath}"
                    : null);

    public static AutomatonGuidanceSeedMetadata? FindByKey(string key) =>
        All.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
}

public sealed record AutomatonGuidanceSeedMetadata(
    string RelativePath,
    string Key,
    string Title,
    string Surface,
    string DocumentKind,
    string Description,
    string? TagsJson,
    bool RequiredByDefault,
    bool IsModelLoadable,
    int SortOrder,
    string? OutputPath);
