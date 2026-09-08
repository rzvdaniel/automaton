using System.Text.RegularExpressions;

namespace Automaton.Core.Constants;

public static partial class HostedRunnerImageReference
{
    public static bool IsImmutable(string? reference)
        => !string.IsNullOrWhiteSpace(reference)
            && ImmutableReferenceRegex().IsMatch(reference.Trim());

    public static void EnsureImmutable(string? reference)
    {
        if (!IsImmutable(reference))
        {
            throw new InvalidOperationException(
                "Hosted Runner provisioning requires an immutable container image reference in the form repository@sha256:<digest>.");
        }
    }

    [GeneratedRegex(@"^[^\s@]+@sha256:[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImmutableReferenceRegex();
}
