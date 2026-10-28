namespace Automaton.Core.Enums;

/// <summary>The only supported application execution profile.</summary>
public static class RunnerApplicationProfiles
{
    public const string MachineManagement = "MachineManagement";

    public static bool TryNormalize(string? profile, out string normalized)
    {
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
        => string.Equals(purpose?.Trim(), RunnerPurposes.Managed, StringComparison.OrdinalIgnoreCase)
            && TryNormalize(profile, out _);
}
