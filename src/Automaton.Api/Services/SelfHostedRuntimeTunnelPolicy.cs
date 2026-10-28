using Automaton.Core.Enums;

namespace Automaton.Api.Services;

public static class SelfHostedRuntimeTunnelPolicy
{
    public static bool AllowsRuntimeTunnel(string? purpose, string? applicationProfile)
        => RunnerPurposes.Normalize(purpose) == RunnerPurposes.Managed
            && RunnerApplicationProfiles.TryNormalize(applicationProfile, out var normalizedProfile)
            && normalizedProfile == RunnerApplicationProfiles.MachineManagement;
}
