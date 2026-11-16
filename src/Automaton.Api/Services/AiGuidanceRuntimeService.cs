using Automaton.AiGuidance;
using Automaton.Api.Constants;

namespace Automaton.Api.Services;

public interface IAiGuidanceRuntimeService
{
    Task<AiGuidanceExportPack> BuildExportPackAsync(CancellationToken cancellationToken = default);
}

/// <summary>Serves AI guidance straight from the files embedded in Automaton.AiGuidance.</summary>
public sealed class AiGuidanceRuntimeService : IAiGuidanceRuntimeService, IAutomatonGuidanceRuntimeProvider
{
    private readonly Lazy<IReadOnlyList<GuidanceEntry>> _entries;

    public AiGuidanceRuntimeService(IAutomatonGuidanceProvider guidanceProvider)
    {
        _entries = new Lazy<IReadOnlyList<GuidanceEntry>>(() => Load(guidanceProvider));
    }

    public Task<AiGuidanceExportPack> BuildExportPackAsync(CancellationToken cancellationToken = default)
    {
        var files = Ordered(_entries.Value.Where(item =>
                item.Metadata.Surface == AutomatonGuidanceSeedCatalog.CliSurface && item.Metadata.OutputPath != null))
            .Select(item => new AiGuidanceExportFile(item.Metadata.OutputPath!, item.Body))
            .ToList();
        return Task.FromResult(new AiGuidanceExportPack(files));
    }

    public Task<AutomatonGuidanceDocument?> GetBasePromptAsync(string surface, CancellationToken cancellationToken = default) =>
        Task.FromResult(Ordered(_entries.Value.Where(item =>
                item.Metadata.RequiredByDefault
                && !item.Metadata.IsModelLoadable
                && item.Metadata.DocumentKind == AiGuidanceDocumentKinds.BasePrompt
                && string.Equals(item.Metadata.Surface, surface, StringComparison.OrdinalIgnoreCase)))
            .Select(ToDocument)
            .FirstOrDefault());

    public Task<IReadOnlyList<AutomatonGuidanceDocument>> GetRequiredGuidanceAsync(string surface, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AutomatonGuidanceDocument>>(Ordered(_entries.Value.Where(item =>
                item.Metadata.IsModelLoadable
                && item.Metadata.RequiredByDefault
                && item.Metadata.DocumentKind == AiGuidanceDocumentKinds.Guidance
                && string.Equals(item.Metadata.Surface, surface, StringComparison.OrdinalIgnoreCase)))
            .Select(ToDocument)
            .ToList());

    public Task<IReadOnlyList<AutomatonGuidanceCatalogItem>> ListLoadableGuidanceAsync(string? surface = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AutomatonGuidanceCatalogItem>>(Ordered(_entries.Value.Where(item =>
                item.Metadata.IsModelLoadable
                && item.Metadata.DocumentKind == AiGuidanceDocumentKinds.Guidance
                && (string.IsNullOrWhiteSpace(surface) || string.Equals(item.Metadata.Surface, surface, StringComparison.OrdinalIgnoreCase))))
            .Select(ToCatalogItem)
            .ToList());

    public Task<AutomatonGuidanceDocument?> GetGuidanceByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult<AutomatonGuidanceDocument?>(null);
        }

        return Task.FromResult(_entries.Value
            .Where(item => item.Metadata.IsModelLoadable
                && item.Metadata.DocumentKind == AiGuidanceDocumentKinds.Guidance
                && string.Equals(item.Metadata.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(ToDocument)
            .FirstOrDefault());
    }

    private static IReadOnlyList<GuidanceEntry> Load(IAutomatonGuidanceProvider provider) =>
        provider.GetSeedSources()
            .Select(source =>
            {
                var metadata = AutomatonGuidanceSeedCatalog.Resolve(source.RelativePath);
                var parsed = AiGuidanceMarkdownFrontMatter.Parse(source.Content);
                return new GuidanceEntry(
                    metadata,
                    string.IsNullOrWhiteSpace(parsed.Title) ? metadata.Title : parsed.Title,
                    string.IsNullOrWhiteSpace(parsed.Description) ? metadata.Description : parsed.Description,
                    parsed.Body);
            })
            .ToList();

    private static IEnumerable<GuidanceEntry> Ordered(IEnumerable<GuidanceEntry> entries) =>
        entries
            .OrderBy(item => item.Metadata.Surface, StringComparer.Ordinal)
            .ThenBy(item => item.Metadata.SortOrder)
            .ThenBy(item => item.Title, StringComparer.Ordinal);

    private static AutomatonGuidanceCatalogItem ToCatalogItem(GuidanceEntry item) =>
        new(
            item.Metadata.Key,
            item.Title,
            item.Metadata.Surface,
            item.Metadata.DocumentKind,
            item.Metadata.TagsJson,
            item.Description,
            item.Metadata.RequiredByDefault,
            item.Metadata.IsModelLoadable,
            item.Metadata.SortOrder,
            1);

    private static AutomatonGuidanceDocument ToDocument(GuidanceEntry item) =>
        new(
            item.Metadata.Key,
            item.Title,
            item.Metadata.Surface,
            item.Metadata.DocumentKind,
            item.Metadata.TagsJson,
            item.Description,
            item.Body,
            item.Metadata.RequiredByDefault,
            item.Metadata.IsModelLoadable,
            item.Metadata.SortOrder,
            1);

    private sealed record GuidanceEntry(
        AutomatonGuidanceSeedMetadata Metadata,
        string Title,
        string Description,
        string Body);
}

public sealed record AiGuidanceExportPack(
    IReadOnlyList<AiGuidanceExportFile> Files);

public sealed record AiGuidanceExportFile(
    string TargetPath,
    string Content);
