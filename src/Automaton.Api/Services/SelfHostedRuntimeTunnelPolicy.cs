using Automaton.Core.Enums;

namespace Automaton.Api.Services;

public static class SelfHostedRuntimeTunnelPolicy
{
    public static bool AllowsRuntimeTunnel(string? purpose, string? applicationProfile)
        => !RunnerProfileRules.IsInfrastructure(purpose)
            && RunnerApplicationProfiles.TryNormalize(applicationProfile, out var normalizedProfile)
            && (normalizedProfile == RunnerApplicationProfiles.Standard
                || normalizedProfile == RunnerApplicationProfiles.PrivilegedHost
                || normalizedProfile == RunnerApplicationProfiles.MachineManagement);
}
