namespace Automaton.AiGuidance;

public sealed record AutomatonGuidanceDocument(
    string Key,
    string RelativePath,
    string Title,
    string Surface,
    string? TagsJson,
    string? Description,
    string ContentMarkdown,
    int Version,
    bool IsDatabaseDocument);
