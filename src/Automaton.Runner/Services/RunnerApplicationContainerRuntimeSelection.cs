using Automaton.Core.Enums;

namespace Automaton.Runner.Console.Services;

internal static class RunnerApplicationContainerRuntimeSelection
{
    internal static bool UsesBroker(string? purpose, string? applicationProfile, string? hostedRunnerId)
        => string.Equals(purpose, RunnerPurposes.Managed, StringComparison.Ordinal)
            && string.Equals(applicationProfile, RunnerApplicationProfiles.Standard, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(hostedRunnerId, out var id)
            && id != Guid.Empty;
}
