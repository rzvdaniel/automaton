namespace Automaton.Core.Enums;

/// <summary>Centralizes validation of the supported Runner profile.</summary>
public static class RunnerProfileRules
{
    public static bool CanExecuteApplications(string? purpose, string? profile, IEnumerable<string>? capabilities)
        => RunnerApplicationProfiles.IsValidForPurpose(profile, purpose)
            && RunnerCapabilities.CanExecuteApplications(capabilities, purpose);

    public static string NormalizeApplicationProfile(string? purpose, string? profile)
    {
        if (!RunnerApplicationProfiles.IsValidForPurpose(profile, purpose))
            throw new ArgumentException("Only Machine Management Runners are supported.");
        return RunnerApplicationProfiles.MachineManagement;
    }

    public static bool IsValid(string? purpose, string? profile)
        => RunnerApplicationProfiles.IsValidForPurpose(profile, purpose);
}
