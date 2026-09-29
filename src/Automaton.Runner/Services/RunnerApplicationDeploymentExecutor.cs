using Automaton.Core.Models;
using Automaton.Core.Services;
using Serilog;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerApplicationDeploymentExecutor(
    RunnerWorkspaceService workspace,
    IAppRuntimeProcessManager runtimeProcessManager,
    AppActionDeploymentService actionDeploymentService,
    IRunnerApplicationBuildExecutor buildExecutor,
    HostedRunnerDeploymentResultJournal resultJournal)
{
    private const long MaximumPackageBytes = 100L * 1024 * 1024;
    private readonly ILogger logger = Log.ForContext<RunnerApplicationDeploymentExecutor>();
    private readonly RunnerDeploymentWorkspace deploymentWorkspace = new(workspace);

    public async Task<DeployAppPackageResult> DeployAsync(
        DeployAppPackageRequest request,
        CancellationToken cancellationToken,
        Action ensureAttemptIsCurrent)
    {
        var attempt = CreateAttempt(request);
        DeployAppPackageResult result;
        try
        {
            result = await ApplyDeploymentAsync(attempt, request, cancellationToken, ensureAttemptIsCurrent);
        }
        catch (Exception exception)
        {
            result = HandleFailure(attempt, request, exception);
        }
        finally
        {
            CleanupStaging(attempt);
        }

        await SaveResultWithoutChangingDeploymentOutcomeAsync(request, result);
        return result;
    }

    private DeploymentAttempt CreateAttempt(DeployAppPackageRequest request)
    {
        var attemptId = request.ExecutionAttemptId ?? Guid.NewGuid();
        return new DeploymentAttempt(
            request.AppId,
            request.DeploymentTargetId,
            workspace.GetDeploymentStagingDirectory(request.AppId, request.DeploymentTargetId, attemptId));
    }

    private async Task<DeployAppPackageResult> ApplyDeploymentAsync(
        DeploymentAttempt attempt,
        DeployAppPackageRequest request,
        CancellationToken cancellationToken,
        Action ensureAttemptIsCurrent)
    {
        var manifest = PreparePackage(attempt, request);
        attempt.PreserveCurrentRuntime = manifest.Application.Runtime.Command is not null;
        var buildResult = await BuildApplicationAsync(attempt, request, manifest.Application, cancellationToken);
        attempt.Logs.AddRange(buildResult.Logs);
        EnsureBuildSucceeded(manifest.Application.Build, buildResult);
        ensureAttemptIsCurrent();
        attempt.SnapshotPath = deploymentWorkspace.CreateSnapshotIfNeeded(attempt.AppId, attempt.TargetId);
        await CleanPreviousDeploymentAsync(attempt, request, manifest.Application);
        var (sourceCount, runtimeCount, candidate) = CopyPackageFiles(attempt, manifest.Application);
        attempt.CandidateRuntimeRoot = candidate.RuntimeRoot;
        attempt.CandidateArtifactRoot = candidate.ArtifactRoot;
        attempt.RuntimeCandidateId = candidate.Id;
        attempt.Logs.Add($"Deployed {sourceCount} source file(s) and {runtimeCount} runtime file(s).");
        var actions = await PrepareActionsAsync(attempt, request, cancellationToken);
        ensureAttemptIsCurrent();
        var applyResult = await runtimeProcessManager.ApplyAsync(
            attempt.AppId, attempt.TargetId, manifest.Application,
            cancellationToken: cancellationToken,
            artifactRoot: candidate.ArtifactRoot,
            candidateId: candidate.Id);
        attempt.CandidatePromoted = candidate.ArtifactRoot is not null && applyResult.PromotionIrreversible;
        if (applyResult.Warning is not null) attempt.Logs.Add(applyResult.Warning);
        return CreateSuccessResult(attempt, request, sourceCount, runtimeCount, actions);
    }

    private async Task SaveResultWithoutChangingDeploymentOutcomeAsync(
        DeployAppPackageRequest request,
        DeployAppPackageResult result)
    {
        try
        {
            await resultJournal.SaveAsync(request, result, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.Warning(exception,
                "Could not persist Hosted Runner deployment result for operation {OperationId}; preserving the runtime outcome",
                request.DeploymentOperationId);
        }
    }

    private static AppDeploymentPackageManifest PreparePackage(
        DeploymentAttempt attempt,
        DeployAppPackageRequest request)
    {
        if (!File.Exists(request.PackagePath))
            throw new FileNotFoundException("Deployment package was not found on the runner.", request.PackagePath);
        if (new FileInfo(request.PackagePath).Length > MaximumPackageBytes)
            throw new InvalidOperationException("Deployment package exceeds the 100 MB limit.");
        Directory.CreateDirectory(attempt.StagingRoot);
        RunnerDeploymentWorkspace.ExtractPackageSafely(request.PackagePath, attempt.StagingRoot);
        var manifest = RunnerDeploymentWorkspace.LoadManifest(attempt.StagingRoot, attempt.AppId);
        RunnerDeploymentWorkspace.ValidateIncomingActions(attempt.StagingRoot, request.SystemInfrastructure);
        return manifest;
    }

    private async Task<ApplicationBuildExecutionResult> BuildApplicationAsync(
        DeploymentAttempt attempt,
        DeployAppPackageRequest request,
        ApplicationContract application,
        CancellationToken cancellationToken)
    {
        var context = request.ExecutionAttemptId is Guid buildAttemptId
            ? new RunnerApplicationBuildContext(attempt.AppId, attempt.TargetId, buildAttemptId)
            : null;
        var build = application.Build;
        if (build.Command is null) return new ApplicationBuildExecutionResult { Succeeded = true };
        var buildWorkspace = Path.Combine(attempt.StagingRoot, "source");
        if (!Directory.Exists(buildWorkspace))
            throw new InvalidOperationException("A build command requires a source workspace in the deployment package.");
        var result = !string.IsNullOrWhiteSpace(build.Image) || !string.IsNullOrWhiteSpace(application.Runtime.Image)
            ? await buildExecutor.ExecuteAsync(build, application.Runtime, buildWorkspace, context, cancellationToken)
            : await ApplicationBuildExecutor.ExecuteAsync(build, buildWorkspace, cancellationToken);
        CopyBuildOutput(attempt, application, buildWorkspace, result);
        return result;
    }

    private static void CopyBuildOutput(
        DeploymentAttempt attempt,
        ApplicationContract application,
        string buildWorkspace,
        ApplicationBuildExecutionResult result)
    {
        if (!result.Succeeded || application.Runtime.Command is null) return;
        var relative = ApplicationBuildArtifactSelector.GetRelativeDirectory(application);
        var output = ApplicationBuildExecutor.ResolveWorkspacePath(buildWorkspace, relative, "runtime artifact directory");
        if (!Directory.Exists(output))
            throw new DirectoryNotFoundException($"Runtime artifact directory does not exist after the build: {output}");
        var packaged = Path.Combine(attempt.StagingRoot, "runtime", "application", Path.GetRelativePath(buildWorkspace, output));
        RunnerDeploymentWorkspace.DeleteDirectoryIfExists(packaged);
        RunnerDeploymentWorkspace.CopyDirectory(output, packaged);
    }

    private async Task CleanPreviousDeploymentAsync(
        DeploymentAttempt attempt,
        DeployAppPackageRequest request,
        ApplicationContract application)
    {
        if (!request.Clean) return;
        if (application.Runtime.Command is null)
        {
            await runtimeProcessManager.StopAsync(attempt.AppId, attempt.TargetId);
            deploymentWorkspace.CleanDeploymentRoots(attempt.AppId, attempt.TargetId);
            return;
        }
        deploymentWorkspace.CleanDeploymentRoots(attempt.AppId, attempt.TargetId, includeRuntime: false);
    }

    private (int SourceCount, int RuntimeCount, RuntimeCandidate Candidate) CopyPackageFiles(
        DeploymentAttempt attempt,
        ApplicationContract application)
    {
        deploymentWorkspace.SynchronizeActionRoots(attempt.AppId, attempt.TargetId, attempt.StagingRoot);
        var sourceCount = RunnerDeploymentWorkspace.CopyTreeIfExists(
            Path.Combine(attempt.StagingRoot, "source"), workspace.GetAppSourceRoot(attempt.AppId, attempt.TargetId));
        return application.Runtime.Command is not null
            ? CopyCandidateRuntime(attempt, sourceCount)
            : CopyCurrentRuntime(attempt, sourceCount);
    }

    private (int, int, RuntimeCandidate) CopyCandidateRuntime(DeploymentAttempt attempt, int sourceCount)
    {
        var id = Guid.NewGuid();
        var runtimeRoot = workspace.GetAppRuntimeCandidateDirectory(attempt.AppId, attempt.TargetId, id);
        var artifactRoot = workspace.GetAppRuntimeCandidateApplicationDirectory(attempt.AppId, attempt.TargetId, id);
        var runtimeCount = RunnerDeploymentWorkspace.CopyTreeIfExists(Path.Combine(attempt.StagingRoot, "runtime"), runtimeRoot);
        Directory.CreateDirectory(artifactRoot);
        return (sourceCount, runtimeCount, new RuntimeCandidate(id, runtimeRoot, artifactRoot));
    }

    private (int, int, RuntimeCandidate) CopyCurrentRuntime(DeploymentAttempt attempt, int sourceCount)
    {
        var runtimeCount = RunnerDeploymentWorkspace.CopyTreeIfExists(
            Path.Combine(attempt.StagingRoot, "runtime"), workspace.GetAppRuntimeCurrentDirectory(attempt.AppId, attempt.TargetId));
        return (sourceCount, runtimeCount, new RuntimeCandidate(null, null, null));
    }

    private async Task<IReadOnlyList<AppActionDeploymentResult>> PrepareActionsAsync(
        DeploymentAttempt attempt,
        DeployAppPackageRequest request,
        CancellationToken cancellationToken)
    {
        var actions = await actionDeploymentService.PrepareAllAsync(
            attempt.AppId, attempt.TargetId, request.SystemInfrastructure, cancellationToken);
        attempt.Logs.AddRange(actions.Select(result => $"Action {result.ActionKey}: {result.Status}."));
        return actions;
    }

    private static void EnsureBuildSucceeded(ApplicationBuildSpec build, ApplicationBuildExecutionResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(BuildFailureMessage(build, result));
    }

    private static string BuildFailureMessage(ApplicationBuildSpec build, ApplicationBuildExecutionResult result)
    {
        return $"Application build {DescribeBuildFailure(build, result)}.";
    }

    private static string DescribeBuildFailure(ApplicationBuildSpec build, ApplicationBuildExecutionResult result)
    {
        if (result.Canceled) return "was canceled";
        if (result.TimedOut) return $"timed out after {build.TimeoutSeconds} seconds";
        return result.Error ?? $"failed with exit code {result.ExitCode?.ToString() ?? "unknown"}";
    }

    private static DeployAppPackageResult CreateSuccessResult(
        DeploymentAttempt attempt,
        DeployAppPackageRequest request,
        int sourceCount,
        int runtimeCount,
        IReadOnlyList<AppActionDeploymentResult> actions)
        => new()
        {
            DeploymentTargetId = request.DeploymentTargetId,
            Success = true,
            Message = $"Deployment package applied for app {attempt.AppId}.",
            SourceFilesDeployed = sourceCount,
            RuntimeFilesDeployed = runtimeCount,
            SnapshotPath = attempt.SnapshotPath,
            Logs = attempt.Logs,
            Actions = actions.ToList()
        };

    private DeployAppPackageResult HandleFailure(
        DeploymentAttempt attempt,
        DeployAppPackageRequest request,
        Exception exception)
    {
        if (attempt.SnapshotPath is not null)
        {
            RestoreAfterFailure(attempt);
        }
        logger.Error(exception, "Failed to deploy app package for app {AppId}", attempt.AppId);
        attempt.Logs.Add(exception.Message);
        return new DeployAppPackageResult
        {
            DeploymentTargetId = request.DeploymentTargetId,
            Success = false,
            Message = exception.Message,
            Logs = attempt.Logs
        };
    }

    private void RestoreAfterFailure(DeploymentAttempt attempt)
    {
        try
        {
            deploymentWorkspace.RestoreSnapshot(
                attempt.AppId, attempt.TargetId, attempt.SnapshotPath!, restoreRuntime: !attempt.PreserveCurrentRuntime);
            attempt.Logs.Add("Deployment failed; the previous App snapshot was restored.");
        }
        catch (Exception restoreException)
        {
            logger.Error(restoreException, "Failed to restore App snapshot after deployment failure for {AppId}", attempt.AppId);
            attempt.Logs.Add("Deployment failed and automatic snapshot restoration also failed.");
        }
    }

    private static void CleanupStaging(DeploymentAttempt attempt)
    {
        TryDeleteUnpromotedRuntime(attempt);
        RunnerDeploymentWorkspace.DeleteDirectoryIfExists(attempt.StagingRoot);
    }

    private static void TryDeleteUnpromotedRuntime(DeploymentAttempt attempt)
    {
        if (!attempt.CandidatePromoted && attempt.CandidateRuntimeRoot is not null)
            RunnerDeploymentWorkspace.DeleteDirectoryIfExists(attempt.CandidateRuntimeRoot);
    }

    private sealed class DeploymentAttempt(Guid appId, Guid targetId, string stagingRoot)
    {
        public Guid AppId { get; } = appId;
        public Guid TargetId { get; } = targetId;
        public string StagingRoot { get; } = stagingRoot;
        public List<string> Logs { get; } = [];
        public string? SnapshotPath { get; set; }
        public string? CandidateRuntimeRoot { get; set; }
        public string? CandidateArtifactRoot { get; set; }
        public Guid? RuntimeCandidateId { get; set; }
        public bool CandidatePromoted { get; set; }
        public bool PreserveCurrentRuntime { get; set; }
    }

    private sealed record RuntimeCandidate(Guid? Id, string? RuntimeRoot, string? ArtifactRoot);
}
