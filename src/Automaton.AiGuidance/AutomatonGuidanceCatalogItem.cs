namespace Automaton.AiGuidance;

public sealed record AutomatonGuidanceCatalogItem(
    string Key,
    string RelativePath,
    string Title,
    string Surface,
    string Category,
    string? Description,
    int Version,
    bool IsDatabaseDocument);
