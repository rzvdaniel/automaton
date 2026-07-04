namespace Automaton.AiGuidance;

public sealed class EmbeddedAutomatonGuidanceRuntimeProvider : IAutomatonGuidanceRuntimeProvider
{
    private static readonly IReadOnlyDictionary<string, EmbeddedGuidanceMetadata> MetadataMap =
        new Dictionary<string, EmbeddedGuidanceMetadata>(StringComparer.Ordinal)
        {
            ["llms/automaton-app-contract-compact.md"] = new("automaton-app-contract-compact", "Automaton App Contract (Compact)", "shared", "Compact always-on Automaton app contract for Studio editing.", """["contract","structure","studio"]"""),
            ["llms/automaton-studio-editor.md"] = new("automaton-studio-editor", "Automaton Studio Editor", "studio", "Studio editor behavior, file editing rules, and verification expectations.", """["editor","files","preview","studio"]"""),
            ["llms/automaton-backend-controllers.md"] = new("automaton-backend-controllers", "Automaton Backend Controllers", "shared", "Python controller rules, response envelope, and backend structure guidance.", """["backend","controllers","api","python"]"""),
            ["llms/automaton-media-assets.md"] = new("automaton-media-assets", "Automaton Media and Static Asset Rules", "shared", "Bundled assets versus runtime media, uploads, and public/static asset handling.", """["assets","media","uploads","images"]"""),
            ["llms/automaton-frontend-structure.md"] = new("automaton-frontend-structure", "Automaton Frontend Structure", "shared", "Frontend structure, relative URLs, PWA rules, and installable app patterns.", """["frontend","pwa","structure","vite"]"""),
            ["llms/automaton-starter-prompts.md"] = new("automaton-starter-prompts", "Automaton Starter Prompts", "cli", "Starter app prompts and initial app creation guidance.", """["starter","scaffold","examples","cli"]"""),
            ["llms/automaton-cli-deploy.md"] = new("automaton-cli-deploy", "Automaton CLI Deploy", "cli", "Deploy and runtime troubleshooting guidance for Automaton apps.", """["deploy","publish","runtime","cli"]"""),
            ["llms/automaton-cli-install.md"] = new("automaton-cli-install", "Automaton CLI Install", "cli", "Automaton CLI installation and bootstrap guidance.", """["install","setup","bootstrap","cli"]"""),
            ["llms/automaton-examples.md"] = new("automaton-examples", "Automaton Examples", "shared", "Longer examples and reference app patterns.", """["examples","patterns","reference"]"""),
            ["llms/automaton-mcp.md"] = new("automaton-mcp", "Automaton MCP", "mcp", "Guidance for MCP-related workflows and integrations.", """["mcp","protocol","tools"]""")
        };

    private readonly IAutomatonGuidanceProvider _guidanceProvider;

    public EmbeddedAutomatonGuidanceRuntimeProvider(IAutomatonGuidanceProvider guidanceProvider)
    {
        _guidanceProvider = guidanceProvider;
    }

    public Task<IReadOnlyList<AutomatonGuidanceCatalogItem>> ListReferencesAsync(string? surface = null, CancellationToken cancellationToken = default)
    {
        var references = _guidanceProvider.GetReferenceSources()
            .Where(source => source.RelativePath.StartsWith("llms/", StringComparison.Ordinal))
            .Select(source =>
            {
                var metadata = ResolveMetadata(source.RelativePath);
                return new AutomatonGuidanceCatalogItem(
                    Key: metadata.Key,
                    RelativePath: source.RelativePath,
                    Title: metadata.Title,
                    Surface: metadata.Surface,
                    TagsJson: metadata.TagsJson,
                    Description: metadata.Description,
                    Version: 0,
                    IsDatabaseDocument: false);
            })
            .ToList();

        if (!string.IsNullOrWhiteSpace(surface))
        {
            references = references.Where(item => string.Equals(item.Surface, surface, StringComparison.Ordinal)).ToList();
        }

        return Task.FromResult<IReadOnlyList<AutomatonGuidanceCatalogItem>>(references);
    }

    public Task<AutomatonGuidanceDocument?> GetReferenceAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = _guidanceProvider.GetReference(relativePath);
            var metadata = ResolveMetadata(relativePath);
            return Task.FromResult<AutomatonGuidanceDocument?>(new AutomatonGuidanceDocument(
                Key: metadata.Key,
                RelativePath: relativePath,
                Title: metadata.Title,
                Surface: metadata.Surface,
                TagsJson: metadata.TagsJson,
                Description: metadata.Description,
                ContentMarkdown: content,
                Version: 0,
                IsDatabaseDocument: false));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult<AutomatonGuidanceDocument?>(null);
        }
    }

    public async Task<AutomatonGuidanceDocument?> GetReferenceByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var reference = (await ListReferencesAsync(cancellationToken: cancellationToken))
            .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        return reference is null
            ? null
            : await GetReferenceAsync(reference.RelativePath, cancellationToken);
    }

    private static EmbeddedGuidanceMetadata ResolveMetadata(string relativePath) =>
        MetadataMap.TryGetValue(relativePath, out var metadata)
            ? metadata
            : new EmbeddedGuidanceMetadata(
                relativePath.Replace("llms/", string.Empty, StringComparison.Ordinal).Replace(".md", string.Empty, StringComparison.Ordinal),
                relativePath,
                "shared",
                null,
                """["reference"]""");

    private sealed record EmbeddedGuidanceMetadata(string Key, string Title, string Surface, string? Description, string? TagsJson);
}
