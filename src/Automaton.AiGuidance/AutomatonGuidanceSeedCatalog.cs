namespace Automaton.AiGuidance;

public static class AutomatonGuidanceSeedCatalog
{
    public const int CurrentSeedVersion = 1;
    private static readonly IReadOnlyDictionary<string, AutomatonGuidanceSeedOverride> OverridesByRelativePath =
        new Dictionary<string, AutomatonGuidanceSeedOverride>(StringComparer.Ordinal)
        {
            ["shared/base/automaton-app-contract-compact.md"] = new(
                "automaton-app-contract-compact",
                "Automaton App Contract (Compact)",
                "Compact always-on Automaton app contract for Studio editing.",
                """["contract","structure","studio"]""",
                RequiredByDefault: true,
                IsModelLoadable: false,
                SortOrder: 0),
            ["studio/guidance/automaton-studio-editor.md"] = new(
                "automaton-studio-editor",
                "Automaton Studio Editor",
                "Studio editor behavior, file editing rules, and verification expectations.",
                """["editor","files","preview","studio"]""",
                RequiredByDefault: true,
                IsModelLoadable: true,
                SortOrder: 100),
            ["shared/guidance/automaton-backend-controllers.md"] = new(
                "automaton-backend-controllers",
                "Automaton Backend Controllers",
                "Python controller rules, response envelope, and backend structure guidance.",
                """["backend","controllers","api","python"]""",
                RequiredByDefault: false,
                IsModelLoadable: true,
                SortOrder: 200),
            ["shared/guidance/automaton-media-assets.md"] = new(
                "automaton-media-assets",
                "Automaton Media and Static Asset Rules",
                "Bundled assets versus runtime media, uploads, and public/static asset handling.",
                """["assets","media","uploads","images"]""",
                RequiredByDefault: false,
                IsModelLoadable: true,
                SortOrder: 200),
            ["shared/guidance/automaton-frontend-structure.md"] = new(
                "automaton-frontend-structure",
                "Automaton Frontend Structure",
                "Frontend structure, relative URLs, PWA rules, and installable app patterns.",
                """["frontend","pwa","structure","vite"]""",
                RequiredByDefault: false,
                IsModelLoadable: true,
                SortOrder: 200),
            ["cli/guidance/automaton-starter-prompts.md"] = new(
                "automaton-starter-prompts",
                "Automaton Starter Prompts",
                "Starter app prompts and initial app creation guidance.",
                """["starter","scaffold","examples","cli"]""",
                RequiredByDefault: false,
                IsModelLoadable: true,
                SortOrder: 200),
            ["cli/guidance/automaton-cli-deploy.md"] = new(
                "automaton-cli-deploy",
                "Automaton CLI Deploy",
                "Deploy and runtime troubleshooting guidance for Automaton apps.",
                """["deploy","publish","runtime","cli"]""",
                RequiredByDefault: false,
                IsModelLoadable: true,
                SortOrder: 200),
            ["cli/guidance/automaton-cli-install.md"] = new(
                "automaton-cli-install",
                "Automaton CLI Install",
                "Automaton CLI installation and bootstrap guidance.",
                """["install","setup","bootstrap","cli"]""",
                RequiredByDefault: false,
                IsModelLoadable: true,
                SortOrder: 200),
            ["shared/guidance/automaton-examples.md"] = new(
                "automaton-examples",
                "Automaton Examples",
                "Longer examples and reference app patterns.",
                """["examples","patterns","reference"]""",
                RequiredByDefault: false,
                IsModelLoadable: true,
                SortOrder: 200),
            ["mcp/guidance/automaton-mcp.md"] = new(
                "automaton-mcp",
                "Automaton MCP",
                "Guidance for MCP-related workflows and integrations.",
                """["mcp","protocol","tools"]""",
                RequiredByDefault: false,
                IsModelLoadable: true,
                SortOrder: 200),
            ["cli/project-files/AGENTS.md"] = new(
                "cli-agents",
                "CLI AGENTS",
                "Root local agent wrapper for Automaton projects.",
                """["wrapper","cli","agents"]""",
                RequiredByDefault: false,
                IsModelLoadable: false,
                SortOrder: 300),
            ["cli/project-files/.automaton/agents/skills/automaton-app/SKILL.md"] = new(
                "cli-skill-automaton-app",
                "CLI Automaton App Skill",
                "Local skill entrypoint for Automaton app workflows.",
                """["wrapper","cli","skill"]""",
                RequiredByDefault: false,
                IsModelLoadable: false,
                SortOrder: 320)
        };

    public static AutomatonGuidanceSeedMetadata Resolve(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/').Trim('/');
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3)
        {
            throw new InvalidOperationException($"Embedded AI guidance seed path '{relativePath}' does not match the expected 'surface/category/...' convention.");
        }

        var surface = segments[0];
        var category = segments[1];
        var configured = OverridesByRelativePath.TryGetValue(normalizedPath, out var overrideValue)
            ? overrideValue
            : null;

        var outputPath = category switch
        {
            "base" => null,
            "guidance" => $".automaton/agents/references/llms/{Path.GetFileName(normalizedPath)}",
            "project-files" => string.Join('/', segments.Skip(2)),
            _ => throw new InvalidOperationException($"Unsupported embedded AI guidance category '{category}' in '{relativePath}'.")
        };

        var documentKind = category == "base" ? "BasePrompt" : "Guidance";

        var fallbackKey = category == "project-files"
            ? BuildProjectFileKey(surface, string.Join('/', segments.Skip(2)))
            : Path.GetFileNameWithoutExtension(normalizedPath);

        var fallbackTitle = category == "project-files"
            ? Path.GetFileName(string.Join('/', segments.Skip(2)))
            : HumanizeKey(fallbackKey);

        return new AutomatonGuidanceSeedMetadata(
            normalizedPath,
            configured?.Key ?? fallbackKey,
            configured?.Title ?? fallbackTitle,
            surface,
            documentKind,
            configured?.Description ?? "Focused Automaton guidance reference.",
            configured?.TagsJson ?? """["reference"]""",
            configured?.RequiredByDefault ?? false,
            configured?.IsModelLoadable ?? (category != "project-files"),
            configured?.SortOrder ?? 200,
            outputPath);
    }

    public static bool HasEmbeddedDefault(string key)
    {
        if (OverridesByRelativePath.Values.Any(item => string.Equals(item.Key, key, StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

    private static string BuildProjectFileKey(string surface, string outputPath)
    {
        var normalized = outputPath
            .Replace('\\', '-')
            .Replace('/', '-')
            .Replace('.', '-')
            .Trim('-')
            .ToLowerInvariant();

        return $"{surface}-{normalized}";
    }

    private static string HumanizeKey(string key) =>
        string.Join(' ', key.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
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

public sealed record AutomatonGuidanceSeedOverride(
    string Key,
    string Title,
    string Description,
    string? TagsJson,
    bool RequiredByDefault,
    bool IsModelLoadable,
    int SortOrder);
