namespace Automaton.AiGuidance;

public sealed record AutomatonGuidanceDocument(
    string Key,
    string RelativePath,
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
