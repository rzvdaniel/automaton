namespace Automaton.AgentOrchestrator.Domain;

public sealed record WorkItem(
    string Id,
    string Identifier,
    string Title,
    string? Description,
    string ProviderLifecycleState,
    IReadOnlyList<string> Labels,
    Uri? Url = null,
    DateTimeOffset? CreatedAtUtc = null,
    DateTimeOffset? UpdatedAtUtc = null,
    string? AssignedTo = null);
