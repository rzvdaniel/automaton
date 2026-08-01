namespace Automaton.Core.Enums;

/// <summary>
/// The approved application execution profile for a non-infrastructure Runner.
/// A null value on existing records is treated as Standard for compatibility.
/// </summary>
public static class RunnerApplicationProfiles
{
    public const string Standard = "Standard";
    public const string PrivilegedHost = "PrivilegedHost";

    public static string Normalize(string? profile)
        => string.Equals(profile, PrivilegedHost, StringComparison.OrdinalIgnoreCase)
            ? PrivilegedHost
            : Standard;

    public static bool IsValidForPurpose(string? profile, string? purpose)
        => string.Equals(RunnerPurposes.Normalize(purpose), RunnerPurposes.Infrastructure, StringComparison.Ordinal)
            ? string.IsNullOrWhiteSpace(profile)
            : string.IsNullOrWhiteSpace(profile) ||
              string.Equals(profile, Standard, StringComparison.OrdinalIgnoreCase) ||
              string.Equals(profile, PrivilegedHost, StringComparison.OrdinalIgnoreCase);
}
