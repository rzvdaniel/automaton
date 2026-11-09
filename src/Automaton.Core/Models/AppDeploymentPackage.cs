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
    public Guid? DeploymentOperationId { get; set; }
    public Guid? ExecutionAttemptId { get; set; }
    public ActionContainerPolicy? ActionContainerPolicy { get; set; }
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
    Guid DeploymentTargetId,
    bool DeleteApplicationStorage = false,
    bool UnpublishStaticSite = false);

public sealed record CleanupAppDeploymentTargetResult(
    Guid AppId,
    Guid DeploymentTargetId,
    bool Success,
    string? Error = null);

/// <summary>Starts an app's running runtime again from its deployed files, so it loads current secrets.</summary>
public sealed record RestartAppRuntimeRequest(Guid AppId, Guid DeploymentTargetId);

/// <summary>Stops an app's runtime without removing its files, secrets or data; the app stays paused until resumed.</summary>
public sealed record PauseAppRuntimeRequest(Guid AppId, Guid DeploymentTargetId);

/// <summary>Starts a paused app's runtime again from its deployed files.</summary>
public sealed record ResumeAppRuntimeRequest(Guid AppId, Guid DeploymentTargetId);

/// <summary><c>Changed</c> is false when the target had nothing to pause or resume.</summary>
public sealed record PauseResumeAppRuntimeResult(Guid AppId, Guid DeploymentTargetId, bool Changed);

/// <summary><c>Restarted</c> is false when the target had no running runtime that takes secrets.</summary>
public sealed record RestartAppRuntimeResult(Guid AppId, Guid DeploymentTargetId, bool Restarted);

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
