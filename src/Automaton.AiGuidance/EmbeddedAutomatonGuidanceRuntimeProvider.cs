namespace Automaton.AiGuidance;

public sealed class EmbeddedAutomatonGuidanceRuntimeProvider : IAutomatonGuidanceRuntimeProvider
{
    private static readonly IReadOnlyDictionary<string, EmbeddedGuidanceMetadata> MetadataMap =
        new Dictionary<string, EmbeddedGuidanceMetadata>(StringComparer.Ordinal)
        {
            ["llms/automaton-app-contract-compact.md"] = new("automaton-app-contract-compact", "Automaton App Contract (Compact)", "shared", "BasePrompt", "Compact always-on Automaton app contract for Studio editing.", """["contract","structure","studio"]""", true, false, 0),
            ["llms/automaton-studio-editor.md"] = new("automaton-studio-editor", "Automaton Studio Editor", "studio", "Guidance", "Studio editor behavior, file editing rules, and verification expectations.", """["editor","files","preview","studio"]""", true, true, 100),
            ["llms/automaton-backend-controllers.md"] = new("automaton-backend-controllers", "Automaton Backend Controllers", "shared", "Guidance", "Python controller rules, response envelope, and backend structure guidance.", """["backend","controllers","api","python"]""", false, true, 200),
            ["llms/automaton-media-assets.md"] = new("automaton-media-assets", "Automaton Media and Static Asset Rules", "shared", "Guidance", "Bundled assets versus runtime media, uploads, and public/static asset handling.", """["assets","media","uploads","images"]""", false, true, 200),
            ["llms/automaton-frontend-structure.md"] = new("automaton-frontend-structure", "Automaton Frontend Structure", "shared", "Guidance", "Frontend structure, relative URLs, PWA rules, and installable app patterns.", """["frontend","pwa","structure","vite"]""", false, true, 200),
            ["llms/automaton-starter-prompts.md"] = new("automaton-starter-prompts", "Automaton Starter Prompts", "cli", "Guidance", "Starter app prompts and initial app creation guidance.", """["starter","scaffold","examples","cli"]""", false, true, 200),
            ["llms/automaton-cli-deploy.md"] = new("automaton-cli-deploy", "Automaton CLI Deploy", "cli", "Guidance", "Deploy and runtime troubleshooting guidance for Automaton apps.", """["deploy","publish","runtime","cli"]""", false, true, 200),
            ["llms/automaton-cli-install.md"] = new("automaton-cli-install", "Automaton CLI Install", "cli", "Guidance", "Automaton CLI installation and bootstrap guidance.", """["install","setup","bootstrap","cli"]""", false, true, 200),
            ["llms/automaton-examples.md"] = new("automaton-examples", "Automaton Examples", "shared", "Guidance", "Longer examples and reference app patterns.", """["examples","patterns","reference"]""", false, true, 200),
            ["llms/automaton-mcp.md"] = new("automaton-mcp", "Automaton MCP", "mcp", "Guidance", "Guidance for MCP-related workflows and integrations.", """["mcp","protocol","tools"]""", false, true, 200)
        };

    private readonly IAutomatonGuidanceProvider _guidanceProvider;

    public EmbeddedAutomatonGuidanceRuntimeProvider(IAutomatonGuidanceProvider guidanceProvider)
    {
        _guidanceProvider = guidanceProvider;
    }

    public Task<AutomatonGuidanceDocument?> GetBasePromptAsync(string surface, CancellationToken cancellationToken = default)
    {
        var reference = _guidanceProvider.GetReferenceSources()
            .Where(source => source.RelativePath.StartsWith("llms/", StringComparison.Ordinal))
            .Select(source => (Source: source, Metadata: ResolveMetadata(source.RelativePath)))
            .FirstOrDefault(item =>
                string.Equals(item.Metadata.Surface, surface, StringComparison.Ordinal)
                && string.Equals(item.Metadata.DocumentKind, "BasePrompt", StringComparison.Ordinal));

        return reference.Source is null
            ? Task.FromResult<AutomatonGuidanceDocument?>(null)
            : GetGuidanceAsync(reference.Source.RelativePath, cancellationToken);
    }

    public async Task<IReadOnlyList<AutomatonGuidanceDocument>> GetRequiredGuidanceAsync(string surface, CancellationToken cancellationToken = default)
    {
        var references = await ListLoadableGuidanceAsync(surface, cancellationToken);
        var required = references.Where(item => item.RequiredByDefault).OrderBy(item => item.SortOrder).ToList();
        var documents = new List<AutomatonGuidanceDocument>(required.Count);
        foreach (var reference in required)
        {
            var document = await GetGuidanceAsync(reference.RelativePath, cancellationToken);
            if (document is not null)
            {
                documents.Add(document);
            }
        }

        return documents;
    }

    public Task<IReadOnlyList<AutomatonGuidanceCatalogItem>> ListLoadableGuidanceAsync(string? surface = null, CancellationToken cancellationToken = default)
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
                    DocumentKind: metadata.DocumentKind,
                    TagsJson: metadata.TagsJson,
                    Description: metadata.Description,
                    RequiredByDefault: metadata.RequiredByDefault,
                    IsModelLoadable: metadata.IsModelLoadable,
                    SortOrder: metadata.SortOrder,
                    Version: 0,
                    IsDatabaseDocument: false);
            })
            .Where(item => item.IsModelLoadable)
            .ToList();

        if (!string.IsNullOrWhiteSpace(surface))
        {
            references = references.Where(item => string.Equals(item.Surface, surface, StringComparison.Ordinal)).ToList();
        }

        return Task.FromResult<IReadOnlyList<AutomatonGuidanceCatalogItem>>(references);
    }

    public Task<AutomatonGuidanceDocument?> GetGuidanceAsync(string relativePath, CancellationToken cancellationToken = default)
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
                DocumentKind: metadata.DocumentKind,
                TagsJson: metadata.TagsJson,
                Description: metadata.Description,
                ContentMarkdown: content,
                RequiredByDefault: metadata.RequiredByDefault,
                IsModelLoadable: metadata.IsModelLoadable,
                SortOrder: metadata.SortOrder,
                Version: 0,
                IsDatabaseDocument: false));
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult<AutomatonGuidanceDocument?>(null);
        }
    }

    public async Task<AutomatonGuidanceDocument?> GetGuidanceByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var reference = (await ListLoadableGuidanceAsync(cancellationToken: cancellationToken))
            .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
        return reference is null
            ? null
            : await GetGuidanceAsync(reference.RelativePath, cancellationToken);
    }

    private static EmbeddedGuidanceMetadata ResolveMetadata(string relativePath) =>
        MetadataMap.TryGetValue(relativePath, out var metadata)
            ? metadata
            : new EmbeddedGuidanceMetadata(
                relativePath.Replace("llms/", string.Empty, StringComparison.Ordinal).Replace(".md", string.Empty, StringComparison.Ordinal),
                relativePath,
                "shared",
                "Guidance",
                null,
                """["reference"]""",
                false,
                true,
                200);

    private sealed record EmbeddedGuidanceMetadata(
        string Key,
        string Title,
        string Surface,
        string DocumentKind,
        string? Description,
        string? TagsJson,
        bool RequiredByDefault,
        bool IsModelLoadable,
        int SortOrder);
}
