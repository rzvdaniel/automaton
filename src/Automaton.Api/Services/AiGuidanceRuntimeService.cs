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
            .ThenBy(item => item.Category)
            .ThenBy(item => item.Title)
            .Select(item => new AiGuidanceCatalogItem(
                item.Key,
                item.RelativePath,
                item.Title,
                item.Surface,
                item.Category,
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
                item.Category,
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
                item.Category,
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
                document.Category,
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
                item.Category,
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
                document.Category,
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
            _dbContext.AiGuidanceDocuments.Add(new AiGuidanceDocument
            {
                Id = Guid.NewGuid(),
                Key = metadata.Key,
                RelativePath = source.RelativePath,
                Title = metadata.Title,
                Surface = metadata.Surface,
                Category = metadata.Category,
                Description = metadata.Description,
                ContentMarkdown = source.Content,
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
            return new AiGuidanceResolvedDocument(
                metadata.Key,
                relativePath,
                metadata.Title,
                metadata.Surface,
                metadata.Category,
                metadata.Description,
                content,
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
                "reference",
                "Focused Automaton guidance reference.");
}

public sealed record AiGuidanceCatalogItem(
    string Key,
    string RelativePath,
    string Title,
    string Surface,
    string Category,
    string? Description,
    int Version,
    bool IsDatabaseDocument);

public sealed record AiGuidanceResolvedDocument(
    string Key,
    string RelativePath,
    string Title,
    string Surface,
    string Category,
    string? Description,
    string ContentMarkdown,
    int Version,
    bool IsDatabaseDocument);

public sealed record GuidanceSeedMetadata(
    string Key,
    string Title,
    string Surface,
    string Category,
    string Description);
