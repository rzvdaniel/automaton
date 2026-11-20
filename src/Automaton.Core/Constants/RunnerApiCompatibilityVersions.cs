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
    /// <summary>The Runner knows the security:read, security:manage and logs:receive app permissions.</summary>
    public const string AppSecurityAndLogPermissions = "7";
    public const string Current = AppSecurityAndLogPermissions;

    /// <summary>The first compatibility version that understands a permission; permissions not listed work on every Runner.</summary>
    public static int RequiredVersionForPermission(string permission) => permission switch
    {
        "security:read" or "security:manage" or "logs:receive" => int.Parse(AppSecurityAndLogPermissions),
        _ => 0,
    };
}
