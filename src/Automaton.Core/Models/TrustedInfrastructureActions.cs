using System.Collections.Frozen;

namespace Automaton.Core.Models;

public static class TrustedInfrastructureActions
{
    public static IReadOnlySet<string> Keys { get; } = new[]
    {
        "provision-hosted-runner",
        "reprovision-hosted-runner",
        "restart-hosted-runner",
        "suspend-hosted-runner",
        "resume-hosted-runner",
        "delete-hosted-runner",
        "measure-hosted-runner-storage",
        HostValidationCommand.ActionKey
    }.ToFrozenSet(StringComparer.Ordinal);

    public static void EnsureAllowed(string actionKey)
    {
        if (!Keys.Contains(actionKey))
            throw new UnauthorizedAccessException("The requested trusted infrastructure Action is not allowlisted.");
    }
}
