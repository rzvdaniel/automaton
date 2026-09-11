using Automaton.Core.Enums;

namespace Automaton.Runner.Console.Services;

internal static class RunnerIdentityPolicy
{
    internal static bool IsManagedHostedRunner(string? purpose, string? hostedRunnerId)
        => string.Equals(purpose, RunnerPurposes.Managed, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(hostedRunnerId, out var id)
            && id != Guid.Empty;
}
