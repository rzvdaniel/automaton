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

    /// <summary>The permissions this Runner release is too old to understand (it would reject the app as "unsupported permission"). A Runner that reports no readable release is not blocked.</summary>
    public static IReadOnlyList<string> PermissionsBeyondRunner(string? runnerVersion, IEnumerable<string>? permissions)
    {
        var text = runnerVersion?.Trim().TrimStart('v', 'V');
        var dash = text?.IndexOfAny(['-', '+']) ?? -1;
        if (dash >= 0) text = text![..dash];
        if (!Version.TryParse(text, out var release)) return [];
        return (permissions ?? []).Where(p => RunnerApiCompatibilityVersions.RequiredReleaseForPermission(p) is { } need && release < need).ToList();
    }

    public static void EnsureSupportsPermissions(Runner runner, IEnumerable<string>? permissions)
    {
        var missing = PermissionsBeyondRunner(runner.Version, permissions);
        if (missing.Count == 0) return;
        throw new InvalidOperationException(
            $"Runner too old: Runner '{runner.Name}' (version {runner.Version ?? "unknown"}) does not know the permission {string.Join(", ", missing)} this app needs. "
            + "Upgrade the Runner (Studio: Runners > Upgrade Runner, or run `automaton runners upgrade`), then deploy again.");
    }

    public static bool SupportsRuntimeTunnelReconnectCommands(string? apiCompatibilityVersion)
        => int.TryParse(apiCompatibilityVersion, out var version)
            && version >= int.Parse(RunnerApiCompatibilityVersions.RuntimeTunnelReconnectCommands);

    /// <summary>The app tunnel is HTTP/2 over a Runner-initiated connection; a Runner that predates it cannot carry app traffic.</summary>
    public static bool SupportsAppTunnelOverHttp2(string? apiCompatibilityVersion)
        => int.TryParse(apiCompatibilityVersion, out var version)
            && version >= int.Parse(RunnerApiCompatibilityVersions.AppTunnelOverHttp2);
}
