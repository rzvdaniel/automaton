namespace Automaton.Api.Constants;

public static class ApplicationEventTypes
{
    public const string AppDeployStarted = "AppDeployStarted";
    public const string AppDeployCompleted = "AppDeployCompleted";
    public const string AppDeployFailed = "AppDeployFailed";

    public const string FrontendDeployStarted = "FrontendDeployStarted";
    public const string FrontendDeployCompleted = "FrontendDeployCompleted";
    public const string FrontendDeployFailed = "FrontendDeployFailed";

    public const string BackendDeployStarted = "BackendDeployStarted";
    public const string BackendDeployCompleted = "BackendDeployCompleted";
    public const string BackendDeployFailed = "BackendDeployFailed";
    public const string ManagedRuntimePreparing = "ManagedRuntimePreparing";
    public const string ManagedRuntimeProvisionFailed = "ManagedRuntimeProvisionFailed";
    public const string ManagedRuntimeWaitingForConnection = "ManagedRuntimeWaitingForConnection";

    public const string AppPublished = "AppPublished";
    public const string AppUnpublished = "AppUnpublished";
    public const string AppPublishFailed = "AppPublishFailed";
    public const string AppUnpublishFailed = "AppUnpublishFailed";

    public const string RestoreApplied = "RestoreApplied";
    public const string RestoreFailed = "RestoreFailed";

    public const string RunnerConnected = "RunnerConnected";
    public const string RunnerDisconnected = "RunnerDisconnected";
}
