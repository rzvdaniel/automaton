namespace Automaton.Core.Models;

public sealed class AppDeploymentPackageManifest
{
    public int Version { get; set; } = 1;
    public string AppId { get; set; } = string.Empty;
    public ApplicationContract Application { get; set; } = ApplicationContractDefaults.Create();

    // These fields remain internal to the pre-Stage-2 runner package path.
    // They are not part of the canonical application contract.
    public bool HasBackend { get; set; }
    public string FrontendRuntimePath { get; set; } = "runtime/frontend";
    public string BackendRuntimePath { get; set; } = "runtime/backend";
    public string FrontendSourcePath { get; set; } = "source/frontend";
    public string BackendSourcePath { get; set; } = "source/backend";
    public string BackendModule { get; set; } = "main:app";
}

public sealed class DeployAppPackageRequest
{
    public Guid AppId { get; set; }
    public Guid DeploymentTargetId { get; set; }
    public string PackagePath { get; set; } = string.Empty;
    public bool Clean { get; set; }
    public bool SystemInfrastructure { get; set; }
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
