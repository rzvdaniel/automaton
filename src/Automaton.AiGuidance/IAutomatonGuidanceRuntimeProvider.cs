namespace Automaton.AiGuidance;

public interface IAutomatonGuidanceRuntimeProvider
{
    Task<IReadOnlyList<AutomatonGuidanceCatalogItem>> ListReferencesAsync(string? surface = null, CancellationToken cancellationToken = default);
    Task<AutomatonGuidanceDocument?> GetReferenceAsync(string relativePath, CancellationToken cancellationToken = default);
    Task<AutomatonGuidanceDocument?> GetReferenceByKeyAsync(string key, CancellationToken cancellationToken = default);
}
