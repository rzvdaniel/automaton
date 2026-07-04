namespace Automaton.AiGuidance;

public sealed record AutomatonGuidanceDocument(
    string Key,
    string RelativePath,
    string Title,
    string Surface,
    string Category,
    string? Description,
    string ContentMarkdown,
    int Version,
    bool IsDatabaseDocument);
