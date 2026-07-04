namespace Automaton.AiGuidance;

public interface IAutomatonGuidanceRuntimeProvider
{
    Task<AutomatonGuidanceDocument?> GetBasePromptAsync(string surface, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AutomatonGuidanceDocument>> GetRequiredGuidanceAsync(string surface, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AutomatonGuidanceCatalogItem>> ListLoadableGuidanceAsync(string? surface = null, CancellationToken cancellationToken = default);
    Task<AutomatonGuidanceDocument?> GetGuidanceByKeyAsync(string key, CancellationToken cancellationToken = default);
}
