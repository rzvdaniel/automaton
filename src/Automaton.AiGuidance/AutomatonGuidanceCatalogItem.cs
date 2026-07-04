namespace Automaton.AiGuidance;

public sealed record AutomatonGuidanceCatalogItem(
    string Key,
    string RelativePath,
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
