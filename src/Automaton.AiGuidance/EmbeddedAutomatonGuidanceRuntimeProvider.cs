namespace Automaton.AiGuidance;

public sealed class EmbeddedAutomatonGuidanceRuntimeProvider : IAutomatonGuidanceRuntimeProvider
{
    private static readonly IReadOnlyDictionary<string, EmbeddedGuidanceMetadata> MetadataMap =
        new Dictionary<string, EmbeddedGuidanceMetadata>(StringComparer.Ordinal)
        {
            ["llms/automaton-app-contract-compact.md"] = new("automaton-app-contract-compact", "Automaton App Contract (Compact)", "shared", "contract", "Compact always-on Automaton app contract for Studio editing."),
            ["llms/automaton-studio-editor.md"] = new("automaton-studio-editor", "Automaton Studio Editor", "studio", "editor", "Studio editor behavior, file editing rules, and verification expectations."),
            ["llms/automaton-backend-controllers.md"] = new("automaton-backend-controllers", "Automaton Backend Controllers", "shared", "backend", "Python controller rules, response envelope, and backend structure guidance."),
            ["llms/automaton-media-assets.md"] = new("automaton-media-assets", "Automaton Media and Static Asset Rules", "shared", "media", "Bundled assets versus runtime media, uploads, and public/static asset handling."),
            ["llms/automaton-frontend-structure.md"] = new("automaton-frontend-structure", "Automaton Frontend Structure", "shared", "frontend", "Frontend structure, relative URLs, PWA rules, and installable app patterns."),
            ["llms/automaton-starter-prompts.md"] = new("automaton-starter-prompts", "Automaton Starter Prompts", "cli", "starter", "Starter app prompts and initial app creation guidance."),
            ["llms/automaton-cli-deploy.md"] = new("automaton-cli-deploy", "Automaton CLI Deploy", "cli", "deploy", "Deploy and runtime troubleshooting guidance for Automaton apps."),
            ["llms/automaton-cli-install.md"] = new("automaton-cli-install", "Automaton CLI Install", "cli", "install", "Automaton CLI installation and bootstrap guidance."),
            ["llms/automaton-examples.md"] = new("automaton-examples", "Automaton Examples", "shared", "examples", "Longer examples and reference app patterns."),
            ["llms/automaton-mcp.md"] = new("automaton-mcp", "Automaton MCP", "mcp", "protocol", "Guidance for MCP-related workflows and integrations.")
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
                    Category: metadata.Category,
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
                Category: metadata.Category,
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
                "reference",
                null);

    private sealed record EmbeddedGuidanceMetadata(string Key, string Title, string Surface, string Category, string? Description);
}
