using Automaton.AiGuidance;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public interface IAiGuidanceRuntimeService
{
    Task<AiGuidanceResolvedDocument?> GetBasePromptAsync(string surface, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiGuidanceCatalogItem>> GetRequiredGuidanceAsync(string surface, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiGuidanceCatalogItem>> ListLoadableGuidanceAsync(string? surface = null, CancellationToken cancellationToken = default);
    Task<AiGuidanceResolvedDocument?> GetGuidanceByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<int> SeedMissingDefaultsAsync(CancellationToken cancellationToken = default);
}

public sealed class AiGuidanceRuntimeService : IAiGuidanceRuntimeService, IAutomatonGuidanceRuntimeProvider
{
    private static readonly IReadOnlyDictionary<string, GuidanceSeedMetadata> SeedCatalog =
        new Dictionary<string, GuidanceSeedMetadata>(StringComparer.Ordinal)
        {
            ["llms/automaton-app-contract-compact.md"] = new("automaton-app-contract-compact", "Automaton App Contract (Compact)", "shared", AiGuidanceDocumentKinds.BasePrompt, "Compact always-on Automaton app contract for Studio editing.", """["contract","structure","studio"]""", true, false, 0),
            ["llms/automaton-studio-editor.md"] = new("automaton-studio-editor", "Automaton Studio Editor", "studio", AiGuidanceDocumentKinds.Guidance, "Studio editor behavior, file editing rules, and verification expectations.", """["editor","files","preview","studio"]""", true, true, 100),
            ["llms/automaton-backend-controllers.md"] = new("automaton-backend-controllers", "Automaton Backend Controllers", "shared", AiGuidanceDocumentKinds.Guidance, "Python controller rules, response envelope, and backend structure guidance.", """["backend","controllers","api","python"]""", false, true, 200),
            ["llms/automaton-media-assets.md"] = new("automaton-media-assets", "Automaton Media and Static Asset Rules", "shared", AiGuidanceDocumentKinds.Guidance, "Bundled assets versus runtime media, uploads, and public/static asset handling.", """["assets","media","uploads","images"]""", false, true, 200),
            ["llms/automaton-frontend-structure.md"] = new("automaton-frontend-structure", "Automaton Frontend Structure", "shared", AiGuidanceDocumentKinds.Guidance, "Frontend structure, relative URLs, PWA rules, and installable app patterns.", """["frontend","pwa","structure","vite"]""", false, true, 200),
            ["llms/automaton-starter-prompts.md"] = new("automaton-starter-prompts", "Automaton Starter Prompts", "cli", AiGuidanceDocumentKinds.Guidance, "Starter app prompts and initial app creation guidance.", """["starter","scaffold","examples","cli"]""", false, true, 200),
            ["llms/automaton-cli-deploy.md"] = new("automaton-cli-deploy", "Automaton CLI Deploy", "cli", AiGuidanceDocumentKinds.Guidance, "Deploy and runtime troubleshooting guidance for Automaton apps.", """["deploy","publish","runtime","cli"]""", false, true, 200),
            ["llms/automaton-cli-install.md"] = new("automaton-cli-install", "Automaton CLI Install", "cli", AiGuidanceDocumentKinds.Guidance, "Automaton CLI installation and bootstrap guidance.", """["install","setup","bootstrap","cli"]""", false, true, 200),
            ["llms/automaton-examples.md"] = new("automaton-examples", "Automaton Examples", "shared", AiGuidanceDocumentKinds.Guidance, "Longer examples and reference app patterns.", """["examples","patterns","reference"]""", false, true, 200),
            ["llms/automaton-mcp.md"] = new("automaton-mcp", "Automaton MCP", "mcp", AiGuidanceDocumentKinds.Guidance, "Guidance for MCP-related workflows and integrations.", """["mcp","protocol","tools"]""", false, true, 200)
        };

    private readonly ApplicationDbContext _dbContext;
    private readonly IAutomatonGuidanceProvider _embeddedGuidanceProvider;
    private readonly ILogger<AiGuidanceRuntimeService> _logger;

    public AiGuidanceRuntimeService(
        ApplicationDbContext dbContext,
        IAutomatonGuidanceProvider embeddedGuidanceProvider,
        ILogger<AiGuidanceRuntimeService> logger)
    {
        _dbContext = dbContext;
        _embeddedGuidanceProvider = embeddedGuidanceProvider;
        _logger = logger;
    }

    public Task<AiGuidanceResolvedDocument?> GetBasePromptAsync(string surface, CancellationToken cancellationToken = default) =>
        ResolveSingleAsync(
            item => item.IsEnabled
                && item.RequiredByDefault
                && !item.IsModelLoadable
                && item.DocumentKind == AiGuidanceDocumentKinds.BasePrompt
                && item.Surface == surface,
            cancellationToken);

    public Task<IReadOnlyList<AiGuidanceCatalogItem>> GetRequiredGuidanceAsync(string surface, CancellationToken cancellationToken = default) =>
        QueryCatalog(item =>
                item.IsEnabled
                && item.IsModelLoadable
                && item.RequiredByDefault
                && item.DocumentKind == AiGuidanceDocumentKinds.Guidance
                && item.Surface == surface)
            .ToListAsync(cancellationToken)
            .ContinueWith<IReadOnlyList<AiGuidanceCatalogItem>>(task => task.Result, cancellationToken);

    public async Task<IReadOnlyList<AiGuidanceCatalogItem>> ListLoadableGuidanceAsync(string? surface = null, CancellationToken cancellationToken = default)
    {
        var query = QueryCatalog(item =>
            item.IsEnabled
            && item.IsModelLoadable
            && item.DocumentKind == AiGuidanceDocumentKinds.Guidance);

        if (!string.IsNullOrWhiteSpace(surface))
        {
            query = query.Where(item => item.Surface == surface);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public Task<AiGuidanceResolvedDocument?> GetGuidanceByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult<AiGuidanceResolvedDocument?>(null);
        }

        return ResolveSingleAsync(
            item => item.IsEnabled
                && item.IsModelLoadable
                && item.DocumentKind == AiGuidanceDocumentKinds.Guidance
                && item.Key == key,
            cancellationToken);
    }

    async Task<AutomatonGuidanceDocument?> IAutomatonGuidanceRuntimeProvider.GetBasePromptAsync(string surface, CancellationToken cancellationToken)
    {
        var document = await GetBasePromptAsync(surface, cancellationToken);
        return document is null ? null : MapRuntimeDocument(document);
    }

    async Task<IReadOnlyList<AutomatonGuidanceDocument>> IAutomatonGuidanceRuntimeProvider.GetRequiredGuidanceAsync(string surface, CancellationToken cancellationToken)
    {
        var documents = await GetRequiredGuidanceAsync(surface, cancellationToken);
        var resolved = new List<AutomatonGuidanceDocument>(documents.Count);
        foreach (var document in documents)
        {
            var item = await GetGuidanceByKeyAsync(document.Key, cancellationToken);
            if (item is not null)
            {
                resolved.Add(MapRuntimeDocument(item));
            }
        }

        return resolved;
    }

    async Task<IReadOnlyList<AutomatonGuidanceCatalogItem>> IAutomatonGuidanceRuntimeProvider.ListLoadableGuidanceAsync(string? surface, CancellationToken cancellationToken)
    {
        var documents = await ListLoadableGuidanceAsync(surface, cancellationToken);
        return documents.Select(MapCatalogDocument).ToList();
    }

    async Task<AutomatonGuidanceDocument?> IAutomatonGuidanceRuntimeProvider.GetGuidanceByKeyAsync(string key, CancellationToken cancellationToken)
    {
        var document = await GetGuidanceByKeyAsync(key, cancellationToken);
        return document is null ? null : MapRuntimeDocument(document);
    }

    public async Task<int> SeedMissingDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existingKeys = await _dbContext.AiGuidanceDocuments
            .AsNoTracking()
            .Select(item => item.Key)
            .ToListAsync(cancellationToken);

        var existingKeySet = existingKeys.ToHashSet(StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var source in _embeddedGuidanceProvider.GetReferenceSources().Where(source => source.RelativePath.StartsWith("llms/", StringComparison.Ordinal)))
        {
            var metadata = ResolveMetadata(source.RelativePath);
            if (existingKeySet.Contains(metadata.Key))
            {
                continue;
            }

            var parsed = AiGuidanceMarkdownFrontMatter.Parse(source.Content);
            var title = string.IsNullOrWhiteSpace(parsed.Title) ? metadata.Title : parsed.Title;
            var description = string.IsNullOrWhiteSpace(parsed.Description) ? metadata.Description : parsed.Description;

            _dbContext.AiGuidanceDocuments.Add(new AiGuidanceDocument
            {
                Id = Guid.NewGuid(),
                Key = metadata.Key,
                Title = title,
                Surface = metadata.Surface,
                DocumentKind = metadata.DocumentKind,
                Description = description,
                TagsJson = metadata.TagsJson,
                ContentMarkdown = AiGuidanceMarkdownFrontMatter.Upsert(source.Content, title, description),
                IsEnabled = true,
                RequiredByDefault = metadata.RequiredByDefault,
                IsModelLoadable = metadata.IsModelLoadable,
                SortOrder = metadata.SortOrder,
                Version = 1,
                CreatedAt = now
            });
            added++;
        }

        if (added > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded {Count} AI guidance documents into the database.", added);
        }

        return added;
    }

    private IQueryable<AiGuidanceCatalogItem> QueryCatalog(System.Linq.Expressions.Expression<Func<AiGuidanceDocument, bool>> predicate) =>
        _dbContext.AiGuidanceDocuments
            .AsNoTracking()
            .Where(predicate)
            .OrderBy(item => item.Surface)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Title)
            .Select(item => new AiGuidanceCatalogItem(
                item.Key,
                item.Title,
                item.Surface,
                item.DocumentKind,
                item.TagsJson,
                item.Description,
                item.RequiredByDefault,
                item.IsModelLoadable,
                item.SortOrder,
                item.Version,
                true));

    private Task<AiGuidanceResolvedDocument?> ResolveSingleAsync(
        System.Linq.Expressions.Expression<Func<AiGuidanceDocument, bool>> predicate,
        CancellationToken cancellationToken) =>
        _dbContext.AiGuidanceDocuments
            .AsNoTracking()
            .Where(predicate)
            .Select(item => new AiGuidanceResolvedDocument(
                item.Key,
                item.Title,
                item.Surface,
                item.DocumentKind,
                item.TagsJson,
                item.Description,
                item.ContentMarkdown,
                item.RequiredByDefault,
                item.IsModelLoadable,
                item.SortOrder,
                item.Version,
                true))
            .FirstOrDefaultAsync(cancellationToken);

    private static GuidanceSeedMetadata ResolveMetadata(string relativePath) =>
        SeedCatalog.TryGetValue(relativePath, out var metadata)
            ? metadata
            : new GuidanceSeedMetadata(
                relativePath.Replace("llms/", string.Empty, StringComparison.Ordinal).Replace(".md", string.Empty, StringComparison.Ordinal),
                relativePath,
                "shared",
                AiGuidanceDocumentKinds.Guidance,
                "Focused Automaton guidance reference.",
                """["reference"]""",
                false,
                true,
                200);

    private static AutomatonGuidanceCatalogItem MapCatalogDocument(AiGuidanceCatalogItem item) =>
        new(
            item.Key,
            item.Title,
            item.Surface,
            item.DocumentKind,
            item.TagsJson,
            item.Description,
            item.RequiredByDefault,
            item.IsModelLoadable,
            item.SortOrder,
            item.Version,
            item.IsDatabaseDocument);

    private static AutomatonGuidanceDocument MapRuntimeDocument(AiGuidanceResolvedDocument item) =>
        new(
            item.Key,
            item.Title,
            item.Surface,
            item.DocumentKind,
            item.TagsJson,
            item.Description,
            item.ContentMarkdown,
            item.RequiredByDefault,
            item.IsModelLoadable,
            item.SortOrder,
            item.Version,
            item.IsDatabaseDocument);
}

public sealed record AiGuidanceCatalogItem(
    string Key,
    string Title,
    string Surface,
    string DocumentKind,
    string? TagsJson,
    string? Description,
    bool RequiredByDefault,
    bool IsModelLoadable,
    int SortOrder,
    int Version,
    bool IsDatabaseDocument);

public sealed record AiGuidanceResolvedDocument(
    string Key,
    string Title,
    string Surface,
    string DocumentKind,
    string? TagsJson,
    string? Description,
    string ContentMarkdown,
    bool RequiredByDefault,
    bool IsModelLoadable,
    int SortOrder,
    int Version,
    bool IsDatabaseDocument);

public sealed record GuidanceSeedMetadata(
    string Key,
    string Title,
    string Surface,
    string DocumentKind,
    string Description,
    string? TagsJson,
    bool RequiredByDefault,
    bool IsModelLoadable,
    int SortOrder);
