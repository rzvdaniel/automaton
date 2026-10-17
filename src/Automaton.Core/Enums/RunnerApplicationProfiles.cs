namespace Automaton.Core.Enums;

/// <summary>
/// The approved application execution profile for a non-infrastructure Runner.
/// A null value on existing records is treated as Standard for compatibility.
/// </summary>
public static class RunnerApplicationProfiles
{
    public const string Standard = "Standard";
    public const string PrivilegedHost = "PrivilegedHost";
    public const string MachineManagement = "MachineManagement";

    public static bool TryNormalize(string? profile, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            normalized = Standard;
            return true;
        }

        if (string.Equals(profile, Standard, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Standard;
            return true;
        }

        if (string.Equals(profile, PrivilegedHost, StringComparison.OrdinalIgnoreCase))
        {
            normalized = PrivilegedHost;
            return true;
        }

        if (string.Equals(profile, MachineManagement, StringComparison.OrdinalIgnoreCase))
        {
            normalized = MachineManagement;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    public static string Normalize(string? profile)
        => TryNormalize(profile, out var normalized)
            ? normalized
            : throw new ArgumentException($"Unknown application Runner profile '{profile}'.", nameof(profile));

    public static bool IsValidForPurpose(string? profile, string? purpose)
        => string.Equals(RunnerPurposes.Normalize(purpose), RunnerPurposes.Infrastructure, StringComparison.Ordinal)
            ? string.IsNullOrWhiteSpace(profile)
            : TryNormalize(profile, out _);
}
