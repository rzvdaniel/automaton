namespace Automaton.Api.Models;

/// <summary>
/// Marketplace listing fields to change. Null leaves a field unchanged; an empty
/// category clears it and an empty tag list removes all tags.
/// </summary>
public sealed record UpdateAppListingRequest(
    string? Summary = null,
    string? Description = null,
    string? Category = null,
    IReadOnlyList<string>? Tags = null);
