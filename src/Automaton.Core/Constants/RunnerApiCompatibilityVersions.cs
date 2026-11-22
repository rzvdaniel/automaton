namespace Automaton.Core.Constants;

/// <summary>
/// Explicit protocol versions exchanged between a Runner and the API.
/// These are protocol capabilities, not application release versions.
/// </summary>
public static class RunnerApiCompatibilityVersions
{
    public const string Legacy = "1";
    public const string ManagedApplicationArtifacts = "2";
    public const string ContainerApplicationExecution = "3";
    public const string HostContainerExecution = "4";
    public const string RuntimeTunnelReconnectCommands = "5";
    public const string AppTunnelOverHttp2 = "6";
    public const string Current = AppTunnelOverHttp2;

    /// <summary>The first Runner release that understands a permission (the compatibility version did not change when they were added); permissions not listed work on every Runner.</summary>
    public static Version? RequiredReleaseForPermission(string permission) => permission switch
    {
        "security:read" => new Version(0, 2, 167),
        "logs:receive" => new Version(0, 2, 168),
        "security:manage" => new Version(0, 2, 173),
        _ => null,
    };
}
