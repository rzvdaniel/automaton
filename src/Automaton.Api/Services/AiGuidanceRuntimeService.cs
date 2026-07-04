using Automaton.AiGuidance;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public interface IAiGuidanceRuntimeService
{
    Task<IReadOnlyList<AiGuidanceCatalogItem>> ListEnabledAsync(string? surface = null, CancellationToken cancellationToken = default);
    Task<AiGuidanceResolvedDocument?> GetEnabledByRelativePathAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<AiGuidanceResolvedDocument?> GetEnabledByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<int> SeedMissingDefaultsAsync(CancellationToken cancellationToken = default);
}

public sealed class AiGuidanceRuntimeService : IAiGuidanceRuntimeService, IAutomatonGuidanceRuntimeProvider
{
    private static readonly IReadOnlyDictionary<string, GuidanceSeedMetadata> SeedCatalog =
        new Dictionary<string, GuidanceSeedMetadata>(StringComparer.Ordinal)
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

    public async Task<IReadOnlyList<AiGuidanceCatalogItem>> ListEnabledAsync(string? surface = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.AiGuidanceDocuments
            .AsNoTracking()
            .Where(item => item.IsEnabled);

        if (!string.IsNullOrWhiteSpace(surface))
        {
            query = query.Where(item => item.Surface == surface);
        }

        var documents = await query
            .OrderBy(item => item.Surface)
            .ThenBy(item => item.Title)
            .Select(item => new AiGuidanceCatalogItem(
                item.Key,
                item.RelativePath,
                item.Title,
                item.Surface,
                item.TagsJson,
                item.Description,
                item.Version,
                true))
            .ToListAsync(cancellationToken);

        return documents;
    }

    public async Task<IReadOnlyList<AutomatonGuidanceCatalogItem>> ListReferencesAsync(string? surface = null, CancellationToken cancellationToken = default)
    {
        var documents = await ListEnabledAsync(surface, cancellationToken);
        return documents
            .Select(item => new AutomatonGuidanceCatalogItem(
                item.Key,
                item.RelativePath,
                item.Title,
                item.Surface,
                item.TagsJson,
                item.Description,
                item.Version,
                item.IsDatabaseDocument))
            .ToList();
    }

    public async Task<AiGuidanceResolvedDocument?> GetEnabledByRelativePathAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var document = await _dbContext.AiGuidanceDocuments
            .AsNoTracking()
            .Where(item => item.IsEnabled && item.RelativePath == relativePath)
            .Select(item => new AiGuidanceResolvedDocument(
                item.Key,
                item.RelativePath,
                item.Title,
                item.Surface,
                item.TagsJson,
                item.Description,
                item.ContentMarkdown,
                item.Version,
                true))
            .FirstOrDefaultAsync(cancellationToken);

        return document ?? TryResolveEmbeddedFallback(relativePath);
    }

    public async Task<AutomatonGuidanceDocument?> GetReferenceAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var document = await GetEnabledByRelativePathAsync(relativePath, cancellationToken);
        return document is null
            ? null
            : new AutomatonGuidanceDocument(
                document.Key,
                document.RelativePath,
                document.Title,
                document.Surface,
                document.TagsJson,
                document.Description,
                document.ContentMarkdown,
                document.Version,
                document.IsDatabaseDocument);
    }

    public async Task<AiGuidanceResolvedDocument?> GetEnabledByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var document = await _dbContext.AiGuidanceDocuments
            .AsNoTracking()
            .Where(item => item.IsEnabled && item.Key == key)
            .Select(item => new AiGuidanceResolvedDocument(
                item.Key,
                item.RelativePath,
                item.Title,
                item.Surface,
                item.TagsJson,
                item.Description,
                item.ContentMarkdown,
                item.Version,
                true))
            .FirstOrDefaultAsync(cancellationToken);

        if (document is not null)
        {
            return document;
        }

        var fallbackPath = SeedCatalog.FirstOrDefault(entry => string.Equals(entry.Value.Key, key, StringComparison.Ordinal)).Key;
        return string.IsNullOrWhiteSpace(fallbackPath) ? null : TryResolveEmbeddedFallback(fallbackPath);
    }

    public async Task<AutomatonGuidanceDocument?> GetReferenceByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var document = await GetEnabledByKeyAsync(key, cancellationToken);
        return document is null
            ? null
            : new AutomatonGuidanceDocument(
                document.Key,
                document.RelativePath,
                document.Title,
                document.Surface,
                document.TagsJson,
                document.Description,
                document.ContentMarkdown,
                document.Version,
                document.IsDatabaseDocument);
    }

    public async Task<int> SeedMissingDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existingPaths = await _dbContext.AiGuidanceDocuments
            .AsNoTracking()
            .Select(item => item.RelativePath)
            .ToListAsync(cancellationToken);

        var existingPathSet = existingPaths.ToHashSet(StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var source in _embeddedGuidanceProvider.GetReferenceSources().Where(source => source.RelativePath.StartsWith("llms/", StringComparison.Ordinal)))
        {
            if (existingPathSet.Contains(source.RelativePath))
            {
                continue;
            }

            var metadata = ResolveMetadata(source.RelativePath);
            var parsed = AiGuidanceMarkdownFrontMatter.Parse(source.Content);
            _dbContext.AiGuidanceDocuments.Add(new AiGuidanceDocument
            {
                Id = Guid.NewGuid(),
                Key = metadata.Key,
                RelativePath = source.RelativePath,
                Title = string.IsNullOrWhiteSpace(parsed.Title) ? metadata.Title : parsed.Title,
                Surface = metadata.Surface,
                Description = string.IsNullOrWhiteSpace(parsed.Description) ? metadata.Description : parsed.Description,
                TagsJson = metadata.TagsJson,
                ContentMarkdown = AiGuidanceMarkdownFrontMatter.Upsert(source.Content, string.IsNullOrWhiteSpace(parsed.Title) ? metadata.Title : parsed.Title, string.IsNullOrWhiteSpace(parsed.Description) ? metadata.Description : parsed.Description),
                IsEnabled = true,
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

    private AiGuidanceResolvedDocument? TryResolveEmbeddedFallback(string relativePath)
    {
        try
        {
            var content = _embeddedGuidanceProvider.GetReference(relativePath);
            var metadata = ResolveMetadata(relativePath);
            var parsed = AiGuidanceMarkdownFrontMatter.Parse(content);
            var title = string.IsNullOrWhiteSpace(parsed.Title) ? metadata.Title : parsed.Title;
            var description = string.IsNullOrWhiteSpace(parsed.Description) ? metadata.Description : parsed.Description;
            return new AiGuidanceResolvedDocument(
                metadata.Key,
                relativePath,
                title,
                metadata.Surface,
                metadata.TagsJson,
                description,
                AiGuidanceMarkdownFrontMatter.Upsert(content, title, description),
                0,
                false);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static GuidanceSeedMetadata ResolveMetadata(string relativePath) =>
        SeedCatalog.TryGetValue(relativePath, out var metadata)
            ? metadata
            : new GuidanceSeedMetadata(
                relativePath.Replace("llms/", string.Empty, StringComparison.Ordinal).Replace(".md", string.Empty, StringComparison.Ordinal),
                relativePath,
                "shared",
                "Focused Automaton guidance reference.",
                """["reference"]""");
}

public sealed record AiGuidanceCatalogItem(
    string Key,
    string RelativePath,
    string Title,
    string Surface,
    string? TagsJson,
    string? Description,
    int Version,
    bool IsDatabaseDocument);

public sealed record AiGuidanceResolvedDocument(
    string Key,
    string RelativePath,
    string Title,
    string Surface,
    string? TagsJson,
    string? Description,
    string ContentMarkdown,
    int Version,
    bool IsDatabaseDocument);

public sealed record GuidanceSeedMetadata(
    string Key,
    string Title,
    string Surface,
    string Description,
    string? TagsJson);
