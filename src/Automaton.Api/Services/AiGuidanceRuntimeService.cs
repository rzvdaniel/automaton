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

        files.AddRange(
        [
            new AiGuidanceExportFile(".automaton/agents/references/llms.txt", BuildLlmsText(documents)),
            new AiGuidanceExportFile(".automaton/agents/references/llms-full.txt", BuildLlmsFullText(documents))
        ]);

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
        var existingKeys = await _dbContext.AiGuidanceDocuments
            .AsNoTracking()
            .Select(item => item.Key)
            .ToListAsync(cancellationToken);

        var existingKeySet = existingKeys.ToHashSet(StringComparer.Ordinal);
        var now = DateTime.UtcNow;
        var added = 0;

        foreach (var source in _embeddedGuidanceProvider.GetSeedSources())
        {
            var metadata = AutomatonGuidanceSeedCatalog.Resolve(source.RelativePath);
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
                OutputPath = metadata.OutputPath,
                ContentMarkdown = parsed.Body,
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

    private static string BuildLlmsText(IReadOnlyList<AiGuidanceResolvedDocument> documents)
    {
        var basePrompt = documents.FirstOrDefault(item =>
            item.DocumentKind == AiGuidanceDocumentKinds.BasePrompt
            && string.Equals(item.Surface, "shared", StringComparison.Ordinal))
            ?? documents.FirstOrDefault(item => item.DocumentKind == AiGuidanceDocumentKinds.BasePrompt);

        var builder = new System.Text.StringBuilder();

        if (basePrompt is not null)
        {
            builder.AppendLine(basePrompt.ContentMarkdown.Trim());
            builder.AppendLine();
        }

        var linkedDocuments = documents
            .Where(item => item.DocumentKind != AiGuidanceDocumentKinds.BasePrompt)
            .Where(item => item.OutputPath?.StartsWith(".automaton/agents/references/llms/", StringComparison.Ordinal) == true)
            .ToList();

        if (linkedDocuments.Count > 0)
        {
            builder.AppendLine("## Linked guidance");
            builder.AppendLine();
            foreach (var document in linkedDocuments)
            {
                builder.Append("- `llms/")
                    .Append(document.Key)
                    .Append(".md`");

                if (!string.IsNullOrWhiteSpace(document.Description))
                {
                    builder.Append(" - ").Append(document.Description);
                }

                builder.AppendLine();
            }
        }

        return builder.ToString().Trim() + Environment.NewLine;
    }

    private static string BuildLlmsFullText(IReadOnlyList<AiGuidanceResolvedDocument> documents)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine(BuildLlmsText(documents).TrimEnd());

        foreach (var document in documents
                     .Where(item => item.DocumentKind != AiGuidanceDocumentKinds.BasePrompt)
                     .Where(item => item.OutputPath?.StartsWith(".automaton/agents/references/llms/", StringComparison.Ordinal) == true))
        {
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
            builder.AppendLine(document.ContentMarkdown.Trim());
        }

        return builder.ToString().Trim() + Environment.NewLine;
    }
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
