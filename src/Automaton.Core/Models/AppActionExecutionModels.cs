using System.Text.Json;

namespace Automaton.Core.Models;

public static class AppModuleTypes
{
    public const string Action = "Action";
}

public static class AppModuleExecutionStatuses
{
    public const string Created = "Created";
    public const string Dispatching = "Dispatching";
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string TimedOut = "TimedOut";
    public const string Cancelled = "Cancelled";
    public const string RunnerUnavailable = "RunnerUnavailable";
    public const string Lost = "Lost";
}

public static class AppModuleTriggerKinds
{
    public const string Manual = "Manual";
    public const string Internal = "Internal";
    public const string Scheduled = "Scheduled";
}

public static class AppModuleExecutionErrorCodes
{
    public const string ProcessFailed = "ProcessFailed";
    public const string InvalidInput = "InvalidInput";
    public const string TimedOut = "TimedOut";
    public const string Cancelled = "Cancelled";
    public const string InvalidResult = "InvalidResult";
    public const string ResultTooLarge = "ResultTooLarge";
    public const string RuntimeUnavailable = "RuntimeUnavailable";
    public const string RunnerUnavailable = "RunnerUnavailable";
    public const string PlatformError = "PlatformError";
    public const string AmbiguousDispatch = "AmbiguousDispatch";
}

public static class InfrastructureCommandResultOutcomes
{
    public const string Accepted = "accepted";
    public const string AlreadyApplied = "already-applied";
    public const string TerminalStale = "terminal-stale";

    public static bool IsAckable(string? outcome) => outcome is Accepted or AlreadyApplied or TerminalStale;
}

public sealed record InfrastructureCommandResultResponse(string Outcome);

public sealed class ExecuteAppActionModuleRequest
{
    public Guid ExecutionId { get; set; }
    public Guid AppId { get; set; }
    public Guid DeploymentTargetId { get; set; }
    public string ActionKey { get; set; } = string.Empty;
    public string? ExpectedDeploymentHash { get; set; }
    public int? TimeoutSeconds { get; set; }
    public bool SystemInfrastructure { get; set; }
    public JsonElement Input { get; set; }
    public Dictionary<string, string> Secrets { get; set; } = [];
    public ActionContainerPolicy? ContainerPolicy { get; set; }
}

public sealed class AppActionExecutionResult
{
    public Guid ExecutionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ExitCode { get; set; }
    public string? ErrorCode { get; set; }
    public string? FailureSummary { get; set; }
    public string? DeploymentHash { get; set; }
    public bool LogsAvailable { get; set; }
    public bool ResultAvailable { get; set; }
    public DateTime? LogsExpireAtUtc { get; set; }
    public DateTime? ResultExpireAtUtc { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}

public sealed record CancelAppModuleExecutionRequest(Guid ExecutionId);
public sealed record ReadAppModuleExecutionArtifactRequest(
    Guid AppId,
    Guid DeploymentTargetId,
    string ActionKey,
    Guid ExecutionId);
public sealed record AppModuleExecutionArtifactResponse(bool Available, string? Content, DateTime? ExpiresAtUtc, string? Message = null);

public sealed record AppActionModuleRequest(Guid AppId, Guid DeploymentTargetId, string ActionKey);
public sealed record AppActionVersionRequest(Guid AppId, Guid DeploymentTargetId, string ActionKey, string Version);
public sealed record AppActionVersionInfo(string Version, string? ContentHash, DateTime CreatedAtUtc, long SizeBytes, bool IsCurrent = false);
public sealed record AppActionVersionsResponse(IReadOnlyList<AppActionVersionInfo> Versions);
public sealed record AppActionVersionOperationResponse(bool Success, string Message, string? ContentHash = null);
public sealed record AppActionModuleInfo(
    string Key,
    string? Name,
    string? Description,
    string Runtime,
    string Entrypoint,
    int TimeoutSeconds,
    string? ContentHash,
    bool Schedulable,
    IReadOnlyList<string> AllowedExecutionScopes,
    int MinimumIntervalSeconds,
    Dictionary<string, object?>? InputSchema);
public sealed record AppActionModulesResponse(IReadOnlyList<AppActionModuleInfo> Actions);
public sealed record PrepareAppActionModuleRequest(
    Guid AppId,
    Guid DeploymentTargetId,
    string ActionKey,
    byte[] SourceZip,
    string ExpectedContentHash,
    bool SystemInfrastructure);
public sealed record SynchronizeAppActionModulesRequest(
    Guid AppId,
    Guid DeploymentTargetId,
    IReadOnlyList<string> ActionKeys);
