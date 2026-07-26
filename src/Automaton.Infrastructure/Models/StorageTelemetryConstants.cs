namespace Automaton.Api.Models;

public static class StorageMeasurementSources
{
    public const string RunnerSelfReported = "RunnerSelfReported";
    public const string InfrastructureRunnerVerified = "InfrastructureRunnerVerified";
}

public static class StorageMeasurementStatuses
{
    public const string NeverMeasured = "NeverMeasured";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Stale = "Stale";
}

public static class HostedRunnerStorageStates
{
    public const string Unknown = "Unknown";
    public const string Healthy = "Healthy";
    public const string Warning = "Warning";
    public const string DeploymentBlocked = "DeploymentBlocked";
    public const string OverLimit = "OverLimit";
    public const string Suspended = "Suspended";
    public const string MeasurementFailed = "MeasurementFailed";
    public const string Stale = "Stale";
}

public static class RunnerHostStorageStates
{
    public const string Unknown = "Unknown";
    public const string Healthy = "Healthy";
    public const string LowSpace = "LowSpace";
    public const string SchedulingBlocked = "SchedulingBlocked";
    public const string Critical = "Critical";
    public const string MeasurementFailed = "MeasurementFailed";
    public const string Stale = "Stale";
}

public static class StorageVerificationStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string TimedOut = "TimedOut";
}

public static class StorageTelemetryActions
{
    public const string MeasureHostedRunnerStorage = "measure-hosted-runner-storage";
}

public static class StorageTelemetryAuditEvents
{
    public const string VerificationRequested = "HostedRunnerStorageVerificationRequested";
    public const string VerificationSucceeded = "HostedRunnerStorageVerificationSucceeded";
    public const string VerificationFailed = "HostedRunnerStorageVerificationFailed";
    public const string HostedRunnerPolicyChanged = "HostedRunnerStoragePolicyChanged";
    public const string RunnerHostPolicyChanged = "RunnerHostStoragePolicyChanged";
}
