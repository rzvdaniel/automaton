using Automaton.Api.Entities;
using Automaton.Core.Constants;

namespace Automaton.Api.Services;

/// <summary>
/// Guards API operations that require an explicitly versioned Runner protocol.
/// </summary>
public static class RunnerProtocolCompatibilityPolicy
{
    public static void EnsureSupportsManagedApplicationArtifacts(Runner runner)
    {
        if (!int.TryParse(runner.ApiCompatibilityVersion, out var version)
            || version < int.Parse(RunnerApiCompatibilityVersions.ManagedApplicationArtifacts))
        {
            throw new InvalidOperationException(
                $"Runner '{runner.Name}' is incompatible with the current application deployment protocol. Upgrade the Runner before deploying.");
        }
    }

    public static void EnsureSupportsContainerApplicationExecution(Runner runner)
    {
        if (!int.TryParse(runner.ApiCompatibilityVersion, out var version)
            || version < int.Parse(RunnerApiCompatibilityVersions.ContainerApplicationExecution))
        {
            throw new InvalidOperationException(
                $"Runner '{runner.Name}' must be upgraded for container-based application execution before deployment.");
        }
    }

    public static bool SupportsRuntimeTunnelReconnectCommands(string? apiCompatibilityVersion)
        => int.TryParse(apiCompatibilityVersion, out var version)
            && version >= int.Parse(RunnerApiCompatibilityVersions.RuntimeTunnelReconnectCommands);
}
