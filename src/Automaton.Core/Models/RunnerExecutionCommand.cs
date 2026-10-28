using Automaton.Core.Enums;

namespace Automaton.Core.Models;

public sealed record RunnerExecutionCommand(
    Guid OperationId,
    Guid RunnerId,
    string? Kind = null,
    string? TargetApiInstanceId = null,
    string? TargetApiColor = null);

public static class RunnerExecutionCommandKinds
{
    public const string ReconnectRuntimeTunnels = "reconnect-runtime-tunnels";
}
public sealed record RunnerExecutionRequest(Guid? FlowId, RunnerCustomStepPayload? Step, AppActionCommand? Action = null,
    SynchronizeApplicationManagedArtifactsRequest? ManagedArtifacts = null, RunnerFileTransferCommand? FileTransfer = null,
    HostContainerExecutionRequest? HostContainer = null);
public sealed record RunnerExecutionAssignment(string Disposition, RunnerExecutionRequest? Request);
public sealed record RunnerExecutionReceipt(string Outcome);

public sealed class RunnerCustomStepPayload
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public List<StepVariable>? CodeInputVariables { get; set; }
    public List<StepVariable>? CodeOutputVariables { get; set; }
}

public sealed class RunnerExecutionResult
{
    public string Outcome { get; set; } = "Failed";
    public string? Error { get; set; }
    public WorkflowStatus WorkflowStatus { get; set; } = WorkflowStatus.Error;
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public string? Output { get; set; }
    public System.Text.Json.JsonElement? ActionResult { get; set; }
    public SynchronizeApplicationManagedArtifactsResult? ManagedArtifactsResult { get; set; }
    public RunnerFileTransferResult? FileTransferResult { get; set; }
    public HostContainerExecutionResponse? HostContainerResult { get; set; }
    public Dictionary<string, string?> OutputVariables { get; set; } = [];
    public List<string> Logs { get; set; } = [];
    public static RunnerExecutionResult Interrupted(string reason) => new()
    {
        Outcome = "Lost", Error = reason, Logs = [$"[Error] {reason}"], Started = DateTime.UtcNow, Finished = DateTime.UtcNow
    };
}

public static class RunnerFileTransferKinds
{
    public const string UploadAppFile = "upload-app-file";
    public const string DownloadAppFile = "download-app-file";
    public const string UploadMedia = "upload-media";
    public const string ReadMediaStats = "read-media-stats";
    public const string ReadMediaMetadata = "read-media-metadata";
    public const string DownloadMedia = "download-media";
    public const string DeployPackage = "deploy-package";
    public const string PublishStaticSite = "publish-static-site";
    public const string UnpublishStaticSite = "unpublish-static-site";
}

public static class RunnerFileTransferBounds
{
    public const long MaximumBytes = 200L * 1024 * 1024;
}

public sealed record RunnerFileTransferCommand(
    string Kind,
    Guid AppId,
    string SessionId,
    string? FileName = null,
    string? ContentType = null,
    string? RelativePath = null,
    long? Length = null,
    string? Sha256 = null,
    Guid? DeploymentTargetId = null,
    bool Clean = false,
    Guid? DeploymentOperationId = null,
    Guid? ExecutionAttemptId = null,
    ActionContainerPolicy? ActionContainerPolicy = null,
    int? MinimumFreeDiskMb = null);

public sealed class RunnerFileTransferResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? SessionId { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? Length { get; set; }
    public string? Sha256 { get; set; }
    public long? TotalBytes { get; set; }
    public int? FileCount { get; set; }
    public int? StatusCode { get; set; }
    public string? RelativePath { get; set; }
    public DeployAppPackageResult? Deployment { get; set; }
}
