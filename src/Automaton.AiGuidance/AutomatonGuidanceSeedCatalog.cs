namespace Automaton.AiGuidance;

public static class AutomatonGuidanceSeedCatalog
{
    public const int CurrentSeedVersion = 1;
    public const string CliSurface = "cli";

    private static readonly IReadOnlyList<AutomatonGuidanceSeedMetadata> Entries =
    [
        new(
            "shared/base/automaton-app-contract.md",
            "automaton-app-contract",
            "Automaton App Contract",
            "shared",
            "BasePrompt",
            "The stack-neutral Automaton application contract.",
            "[\"contract\",\"runtime\",\"storage\"]",
            RequiredByDefault: true,
            IsModelLoadable: false,
            SortOrder: 0,
            OutputPath: null),
        new(
            "shared/guidance/automaton-actions.md",
            "automaton-actions",
            "Automaton Actions",
            "shared",
            "Guidance",
            "The Automaton Action manifest and execution contract.",
            "[\"actions\",\"execution\"]",
            RequiredByDefault: false,
            IsModelLoadable: true,
            SortOrder: 100,
            OutputPath: null),
        new(
            "studio/guidance/automaton-studio-editor.md",
            "automaton-studio-editor",
            "Automaton Studio Editor",
            "studio",
            "Guidance",
            "Rules for editing an app inside Automaton Studio.",
            "[\"editor\",\"studio\"]",
            RequiredByDefault: true,
            IsModelLoadable: true,
            SortOrder: 100,
            OutputPath: null),
        new(
            "mcp/guidance/automaton-mcp.md",
            "automaton-mcp",
            "Automaton MCP",
            "mcp",
            "Guidance",
            "The boundary between Automaton MCP inspection and CLI delivery.",
            "[\"mcp\",\"tools\"]",
            RequiredByDefault: false,
            IsModelLoadable: true,
            SortOrder: 100,
            OutputPath: null),
        new(
            "cli/project-files/AGENTS.md",
            "cli-agents",
            "CLI AGENTS",
            "cli",
            "Guidance",
            "The generated Automaton app entry point for coding agents.",
            "[\"cli\",\"agents\"]",
            RequiredByDefault: false,
            IsModelLoadable: false,
            SortOrder: 300,
            OutputPath: "AGENTS.md"),
        new(
            "cli/project-files/.automaton/agents/skills/automaton-app/SKILL.md",
            "cli-skill-automaton-app",
            "CLI Automaton App Skill",
            "cli",
            "Guidance",
            "The generated local Automaton app contract and workflow.",
            "[\"cli\",\"skill\"]",
            RequiredByDefault: false,
            IsModelLoadable: false,
            SortOrder: 310,
            OutputPath: ".automaton/agents/skills/automaton-app/SKILL.md")
    ];

    private static readonly IReadOnlyDictionary<string, AutomatonGuidanceSeedMetadata> EntriesByPath =
        Entries.ToDictionary(item => item.RelativePath, StringComparer.Ordinal);

    public static IReadOnlyList<AutomatonGuidanceSeedMetadata> GetEntries() => Entries;

    public static AutomatonGuidanceSeedMetadata Resolve(string relativePath)
    {
        var normalizedPath = Normalize(relativePath);
        if (!EntriesByPath.TryGetValue(normalizedPath, out var metadata))
        {
            throw new InvalidOperationException(
                $"Embedded AI guidance seed path '{relativePath}' is not explicitly registered.");
        }

        return metadata;
    }

    public static bool HasEmbeddedDefault(string key) =>
        Entries.Any(item => string.Equals(item.Key, key, StringComparison.Ordinal));

    private static string Normalize(string relativePath) =>
        relativePath.Replace('\\', '/').Trim('/');
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
