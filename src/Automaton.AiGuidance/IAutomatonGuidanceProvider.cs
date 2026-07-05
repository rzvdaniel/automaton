namespace Automaton.AiGuidance;

public interface IAutomatonGuidanceProvider
{
    IReadOnlyList<GuidanceSource> GetSeedSources();
}
