namespace Automaton.Core.Models;

public sealed class AppDeploymentPackageManifest
{
    public int Version { get; set; } = 1;
    public string AppId { get; set; } = string.Empty;
    public ApplicationContract Application { get; set; } = ApplicationContractDefaults.Create();
}

public sealed class DeployAppPackageRequest
{
    public Guid AppId { get; set; }
    public Guid DeploymentTargetId { get; set; }
    public string PackagePath { get; set; } = string.Empty;
    public bool Clean { get; set; }
    public bool SystemInfrastructure { get; set; }
    public Guid? DeploymentOperationId { get; set; }
    public Guid? ExecutionAttemptId { get; set; }
}

public sealed class DeployAppPackageResult
{
    public Guid DeploymentTargetId { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int SourceFilesDeployed { get; set; }
    public int RuntimeFilesDeployed { get; set; }
    public int RunnerCount { get; set; }
    public string? SnapshotPath { get; set; }
    public List<string>? Logs { get; set; }
    public List<AppActionDeploymentResult> Actions { get; set; } = [];
}

public sealed class AppActionDeploymentResult
{
    public string ActionKey { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ContentHash { get; set; }
    public string? Message { get; set; }
}

public sealed record CleanupAppDeploymentTargetRequest(
    Guid AppId,
    Guid DeploymentTargetId);

public sealed record CleanupAppDeploymentTargetResult(
    Guid AppId,
    Guid DeploymentTargetId,
    bool Success,
    string? Error = null);

public sealed record ApplicationManagedArtifactFile(
    string RelativePath,
    byte[] Content);

public sealed record SynchronizeApplicationManagedArtifactsRequest(
    Guid AppId,
    Guid DeploymentTargetId,
    IReadOnlyList<ApplicationManagedArtifactFile> Files);

public sealed record SynchronizeApplicationManagedArtifactsResult(
    Guid AppId,
    Guid DeploymentTargetId,
    bool Success,
    string? Error = null);
