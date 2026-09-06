namespace Automaton.Api.Models;

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
    public bool CanDeployApplicationFromStudio { get; init; }
}
