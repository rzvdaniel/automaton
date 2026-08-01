using Automaton.AiGuidance;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace Automaton.Api.Services;

public interface IAiGuidanceRuntimeService
{
    Task<AiGuidanceResolvedDocument?> GetBasePromptAsync(string surface, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiGuidanceCatalogItem>> GetRequiredGuidanceAsync(string surface, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiGuidanceCatalogItem>> ListLoadableGuidanceAsync(string? surface = null, CancellationToken cancellationToken = default);
    Task<AiGuidanceResolvedDocument?> GetGuidanceByKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<AiGuidanceExportPack> BuildExportPackAsync(CancellationToken cancellationToken = default);
    Task<int> SeedMissingDefaultsAsync(CancellationToken cancellationToken = default);
}

public sealed class AiGuidanceRuntimeService : IAiGuidanceRuntimeService, IAutomatonGuidanceRuntimeProvider
{
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

    public async Task<AiGuidanceExportPack> BuildExportPackAsync(CancellationToken cancellationToken = default)
    {
        var documents = await _dbContext.AiGuidanceDocuments
            .AsNoTracking()
            .Where(item => item.IsEnabled)
            .OrderBy(item => item.Surface)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Title)
            .Select(item => new AiGuidanceResolvedDocument(
                item.Key,
                item.Title,
                item.Surface,
                item.DocumentKind,
                item.TagsJson,
                item.Description,
                item.OutputPath,
                item.ContentMarkdown,
                item.RequiredByDefault,
                item.IsModelLoadable,
                item.SortOrder,
                item.Version,
                true))
            .ToListAsync(cancellationToken);

        var files = documents
            .Where(document => !string.IsNullOrWhiteSpace(document.OutputPath))
            .Select(document => new AiGuidanceExportFile(document.OutputPath!, document.ContentMarkdown))
            .ToList();

        return new AiGuidanceExportPack(files);
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
        var existingDocuments = (await _dbContext.AiGuidanceDocuments.ToListAsync(cancellationToken))
            .ToDictionary(item => item.Key, StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        var changed = 0;
        var seedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in _embeddedGuidanceProvider.GetSeedSources())
        {
            var metadata = AutomatonGuidanceSeedCatalog.Resolve(source.RelativePath);
            seedKeys.Add(metadata.Key);

            var parsed = AiGuidanceMarkdownFrontMatter.Parse(source.Content);
            var title = string.IsNullOrWhiteSpace(parsed.Title) ? metadata.Title : parsed.Title;
            var description = string.IsNullOrWhiteSpace(parsed.Description) ? metadata.Description : parsed.Description;

            var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parsed.Body))).ToLowerInvariant();
            var isNew = !existingDocuments.TryGetValue(metadata.Key, out var document);
            if (isNew)
            {
                document = new AiGuidanceDocument { Id = Guid.NewGuid(), Key = metadata.Key, IsEnabled = true, CreatedAt = now };
                _dbContext.AiGuidanceDocuments.Add(document);
            }

            var requiresSynchronization = isNew
                || !string.Equals(document!.Source, AiGuidanceSources.Seed, StringComparison.Ordinal)
                || document.SeedVersion != AutomatonGuidanceSeedCatalog.CurrentSeedVersion
                || !string.Equals(document.SeedContentHash, contentHash, StringComparison.Ordinal)
                || !string.Equals(document.Title, title, StringComparison.Ordinal)
                || !string.Equals(document.Description, description, StringComparison.Ordinal)
                || !string.Equals(document.Surface, metadata.Surface, StringComparison.Ordinal)
                || !string.Equals(document.DocumentKind, metadata.DocumentKind, StringComparison.Ordinal)
                || !string.Equals(document.TagsJson, metadata.TagsJson, StringComparison.Ordinal)
                || !string.Equals(document.OutputPath, metadata.OutputPath, StringComparison.Ordinal)
                || document.RequiredByDefault != metadata.RequiredByDefault
                || document.IsModelLoadable != metadata.IsModelLoadable
                || document.SortOrder != metadata.SortOrder
                || document.IsSeedRetired;

            if (!requiresSynchronization)
            {
                continue;
            }

            document.Title = title;
            document.Surface = metadata.Surface;
            document.DocumentKind = metadata.DocumentKind;
            document.Description = description;
            document.TagsJson = metadata.TagsJson;
            document.OutputPath = metadata.OutputPath;
            document.ContentMarkdown = parsed.Body;
            document.RequiredByDefault = metadata.RequiredByDefault;
            document.IsModelLoadable = metadata.IsModelLoadable;
            document.SortOrder = metadata.SortOrder;
            document.Source = "Seed";
            document.SeedVersion = AutomatonGuidanceSeedCatalog.CurrentSeedVersion;
            document.SeedContentHash = contentHash;
            document.LastSeedSynchronizedAt = now;
            document.IsSeedRetired = false;
            document.Version = isNew ? 1 : document.Version + 1;
            document.UpdatedAt = now;
            changed++;
        }

        foreach (var document in existingDocuments.Values.Where(item =>
            item.Source == AiGuidanceSources.Seed &&
            !item.IsSeedRetired &&
            !seedKeys.Contains(item.Key)))
        {
            document.IsSeedRetired = true;
            document.IsEnabled = false;
            document.LastSeedSynchronizedAt = now;
            document.Version++;
            document.UpdatedAt = now;
            changed++;
        }

        if (changed > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Synchronized {Count} embedded AI guidance documents into the database.", changed);
        }

        return changed;
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
                item.OutputPath,
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
                item.OutputPath,
                item.ContentMarkdown,
                item.RequiredByDefault,
                item.IsModelLoadable,
                item.SortOrder,
                item.Version,
                true))
            .FirstOrDefaultAsync(cancellationToken);

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
    string? OutputPath,
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
    string? OutputPath,
    string ContentMarkdown,
    bool RequiredByDefault,
    bool IsModelLoadable,
    int SortOrder,
    int Version,
    bool IsDatabaseDocument);

public sealed record AiGuidanceExportPack(
    IReadOnlyList<AiGuidanceExportFile> Files);

public sealed record AiGuidanceExportFile(
    string TargetPath,
    string Content);
