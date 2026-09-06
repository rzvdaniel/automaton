namespace Automaton.Api.Models;

public static class AppFrontendDeployModes
{
    public const string None = "none";
    public const string StaticCopy = "static-copy";
    public const string BuildRequired = "build-required";
}

public static class AppDeployStates
{
    public const string Ready = "ready";
    public const string PreparingManagedRuntime = "preparingManagedRuntime";
    public const string ManagedRunnerProvisionFailed = "managedRunnerProvisionFailed";
    public const string ManagedRunnerWaitingForConnection = "managedRunnerWaitingForConnection";
    public const string ManagedRuntimeUnavailable = "managedRuntimeUnavailable";
}

public sealed class AppDeployCapabilitiesResponse
{
    public bool CanDeployAppFromStudio { get; init; }
    public bool CanDeployFrontendFromStudio { get; init; }
    public bool CanDeployBackendFromStudio { get; init; }
    public string FrontendDeployMode { get; init; } = AppFrontendDeployModes.None;
    public string? FrontendDeployBlockReason { get; init; }
}

public sealed record BackendScriptFile(
    string RelativePath,
    string ControllerName,
    byte[] Content,
    string Language);
