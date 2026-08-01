namespace Automaton.Core.Enums;

/// <summary>
/// Centralizes Runner classification from persisted purpose/profile data.
/// Hosted status is derived by the API from HostedRunner.RunnerId.
/// </summary>
public static class RunnerProfileRules
{
    public static bool IsInfrastructure(string? purpose)
        => RunnerPurposes.Normalize(purpose) == RunnerPurposes.Infrastructure;

    public static bool CanExecuteApplications(string? purpose, string? profile, IEnumerable<string>? capabilities)
        => !IsInfrastructure(purpose)
            && RunnerCapabilities.CanExecuteApplications(capabilities, purpose);

    public static bool IsPrivilegedHost(string? purpose, string? profile)
        => !IsInfrastructure(purpose)
            && RunnerApplicationProfiles.Normalize(profile) == RunnerApplicationProfiles.PrivilegedHost;

    public static bool CanReceiveAutomaticApplicationAssignment(string? purpose, string? profile)
        => !IsInfrastructure(purpose) && !IsPrivilegedHost(purpose, profile);

    public static string? NormalizeApplicationProfile(string? purpose, string? profile)
        => IsInfrastructure(purpose) ? null : RunnerApplicationProfiles.Normalize(profile);

    public static bool IsValid(string? purpose, string? profile)
        => RunnerApplicationProfiles.IsValidForPurpose(profile, purpose);
}
