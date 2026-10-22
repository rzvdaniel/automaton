using System.Collections.Frozen;

namespace Automaton.Core.Models;

public static class TrustedInfrastructureActions
{
    public static IReadOnlySet<string> Keys { get; } = new[]
    {
        HostValidationCommand.ActionKey
    }.ToFrozenSet(StringComparer.Ordinal);

    public static void EnsureAllowed(string actionKey)
    {
        if (!Keys.Contains(actionKey))
            throw new UnauthorizedAccessException("The requested trusted infrastructure Action is not allowlisted.");
    }
}
