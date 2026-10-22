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

public static class HostedRunnerEnforcementStates
{
    public const string None = "None";
    public const string GracePeriod = "GracePeriod";
    public const string SuspensionPending = "SuspensionPending";
    public const string Suspended = "Suspended";
    public const string ResumePending = "ResumePending";
    public const string Recovered = "Recovered";
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
    public const string Dispatching = "Dispatching";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string TimedOut = "TimedOut";
}

public static class StorageTelemetryAuditEvents
{
    public const string RunnerHostPolicyChanged = "RunnerHostStoragePolicyChanged";
}
