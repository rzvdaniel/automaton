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
    public const string Current = HostContainerExecution;
}
