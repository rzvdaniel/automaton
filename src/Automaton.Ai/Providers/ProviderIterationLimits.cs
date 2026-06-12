namespace Automaton.Ai.Providers;

internal static class ProviderIterationLimits
{
    private const int DefaultIterations = 25;

    internal static int Resolve(int configuredIterations)
        => configuredIterations > 0 ? configuredIterations : DefaultIterations;
}
