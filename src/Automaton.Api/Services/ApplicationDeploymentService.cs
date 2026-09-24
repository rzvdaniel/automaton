using System.IO.Compression;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Owns the generic application package deployment path.
/// Controller and Action compatibility paths deliberately live elsewhere.
/// </summary>
public sealed class ApplicationDeploymentService
{
    private readonly ApplicationDbContext dataContext;
    private readonly AppFileStorageService fileStorageService;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly FileUploadRegistry uploadRegistry;
    private readonly ManagedRunnerBootstrapService managedRunnerBootstrapService;
    private readonly AppRunnerAssignmentPolicy runnerAssignmentPolicy;
    private readonly IAppDeploymentTargetResolver deploymentTargetResolver;
    private readonly AppDeploymentTargetStateService deploymentTargetStateService;
    private readonly BackendRouteConfigService backendRouteConfigService;
    private readonly ConnectedFlowArtifactService connectedFlowArtifactService;
    private readonly Serilog.ILogger logger;
    private readonly ApiInstanceIdentityService apiInstanceIdentityService;

    public ApplicationDeploymentService(
        ApplicationDbContext dataContext,
        AppFileStorageService fileStorageService,
        IHubContext<AutomatonHub> automatonHub,
        FileUploadRegistry uploadRegistry,
        ManagedRunnerBootstrapService managedRunnerBootstrapService,
        AppRunnerAssignmentPolicy runnerAssignmentPolicy,
        IAppDeploymentTargetResolver deploymentTargetResolver,
        AppDeploymentTargetStateService deploymentTargetStateService,
        BackendRouteConfigService backendRouteConfigService,
        ConnectedFlowArtifactService connectedFlowArtifactService,
        ApiInstanceIdentityService apiInstanceIdentityService)
    {
        this.dataContext = dataContext;
        this.fileStorageService = fileStorageService;
        this.automatonHub = automatonHub;
        this.uploadRegistry = uploadRegistry;
        this.managedRunnerBootstrapService = managedRunnerBootstrapService;
        this.runnerAssignmentPolicy = runnerAssignmentPolicy;
        this.deploymentTargetResolver = deploymentTargetResolver;
        this.deploymentTargetStateService = deploymentTargetStateService;
        this.backendRouteConfigService = backendRouteConfigService;
        this.connectedFlowArtifactService = connectedFlowArtifactService;
        this.apiInstanceIdentityService = apiInstanceIdentityService;
        logger = Serilog.Log.ForContext<ApplicationDeploymentService>();
    }

    public async Task<DeployPackageResponse> DeployPackageAsync(
        Guid appId,
        Guid ownerId,
        string? targetSlug,
        IFormFile file,
        bool clean,
        CancellationToken cancellationToken,
        Guid? deploymentOperationId = null,
        Guid? executionAttemptId = null)
    {
        if (file.Length <= 0)
            throw new InvalidOperationException("A deployment package file is required.");

        var packageApplication = await DeployPackageContractService.ReadAsync(file, appId, cancellationToken);
        if (string.IsNullOrWhiteSpace(targetSlug))
        {
            var bootstrap = await managedRunnerBootstrapService.EnsureRunnerAssignmentAsync(appId, ownerId, cancellationToken);
            if (!bootstrap.IsReady)
            {
                return new DeployPackageResponse
                {
                    Success = false,
                    State = bootstrap.State,
                    RequiresRetry = bootstrap.RequiresRetry,
                    Message = bootstrap.Message
                };
            }
        }

        var target = await deploymentTargetResolver.ResolveAsync(appId, targetSlug, ownerId, cancellationToken);
        await deploymentTargetStateService.MarkStartedAsync(
            appId, target.Id, cancellationToken, deploymentOperationId, executionAttemptId);
        var targetStateFinalized = false;
        var pendingRouteApplied = false;

        try
        {
            var runner = await runnerAssignmentPolicy.GetAssignedRunnerAsync(
                appId,
                ownerId,
                requireOnline: true,
                targetSlug: target.Slug,
                cancellationToken: cancellationToken);
            if (runner is null || string.IsNullOrWhiteSpace(runner.ConnectionId))
                throw new InvalidOperationException("No connected runner is assigned to this app.");

            await EnsureRunnerOwnershipAsync(runner, cancellationToken);
            RunnerProtocolCompatibilityPolicy.EnsureSupportsManagedApplicationArtifacts(runner);
            if (packageApplication.Runtime.Command is not null || packageApplication.Build.Command is not null)
                RunnerProtocolCompatibilityPolicy.EnsureSupportsContainerApplicationExecution(runner);

            var completion = await UploadPackageToRunnerAsync(appId, runner, file, cancellationToken);
            await EnsureRunnerOwnershipAsync(runner, cancellationToken);
            var result = await automatonHub.Clients.Client(runner.ConnectionId)
                .InvokeAsync<DeployAppPackageResult>(
                    AutomatonHubMethods.DeployAppPackage,
                    new DeployAppPackageRequest
                    {
                        AppId = appId,
                        DeploymentTargetId = target.Id,
                        PackagePath = completion.LocalPath,
                        Clean = clean,
                        DeploymentOperationId = deploymentOperationId,
                        ExecutionAttemptId = executionAttemptId,
                        SystemInfrastructure = InfrastructureAppTrust.IsTrustedApp(appId)
                            && string.Equals(runner.Purpose, RunnerPurposes.Infrastructure, StringComparison.Ordinal)
                    },
                    CancellationToken.None);

            ValidateDeploymentTargetResponse(target.Id, result.DeploymentTargetId);
            if (!result.Success)
            {
                await deploymentTargetStateService.MarkCompletedAsync(
                    appId,
                    target.Id,
                    success: false,
                    cancellationToken: CancellationToken.None,
                    deploymentOperationId: deploymentOperationId,
                    executionAttemptId: executionAttemptId);
                targetStateFinalized = true;
            }

            if (result.Success)
            {
                await connectedFlowArtifactService.SynchronizeAsync(
                    appId,
                    target.Id,
                    ownerId,
                    [runner],
                    cancellationToken);
                await DeployPackageContractService.PersistAsync(dataContext, appId, packageApplication, cancellationToken);
                await PersistDeployPackageSourceAsync(appId, ownerId, file, cancellationToken);
                await fileStorageService.UpdateAppStorageMetadataAsync(ownerId, appId, metadata =>
                {
                    metadata.DevelopmentMode = AppDevelopmentModes.Local;
                });

                await backendRouteConfigService.RefreshAsync(
                    cancellationToken,
                    target.Id,
                    packageApplication.Runtime.Command is not null);
                pendingRouteApplied = true;

                await deploymentTargetStateService.MarkCompletedAsync(
                    appId,
                    target.Id,
                    success: true,
                    cancellationToken: CancellationToken.None,
                    genericRuntimeDeployed: packageApplication.Runtime.Command is not null,
                    deploymentOperationId: deploymentOperationId,
                    executionAttemptId: executionAttemptId);
                targetStateFinalized = true;
            }

            return new DeployPackageResponse
            {
                DeploymentTargetId = target.Id,
                TargetSlug = target.Slug,
                Success = result.Success,
                RunnerCount = result.Success ? 1 : 0,
                SourceFilesDeployed = result.SourceFilesDeployed,
                RuntimeFilesDeployed = result.RuntimeFilesDeployed,
                SnapshotPath = result.SnapshotPath,
                Message = result.Message,
                Logs = result.Logs,
                Actions = result.Actions
            };
        }
        catch (DeploymentOperationRunnerOwnershipChangedException)
        {
            // Runner topology changes are recoverable; preserve the target's
            // Deploying markers for the durable operation retry.
            throw;
        }
        catch (Exception deploymentException)
        {
            await HandleDeploymentFailureAsync(
                appId,
                target.Id,
                deploymentOperationId,
                executionAttemptId,
                targetStateFinalized,
                pendingRouteApplied,
                deploymentException);
            throw;
        }
    }

    private async Task HandleDeploymentFailureAsync(
        Guid appId,
        Guid deploymentTargetId,
        Guid? deploymentOperationId,
        Guid? executionAttemptId,
        bool targetStateFinalized,
        bool pendingRouteApplied,
        Exception deploymentException)
    {
        if (!targetStateFinalized)
        {
            await TryMarkDeploymentFailedAsync(appId, deploymentTargetId, deploymentOperationId, executionAttemptId);
        }

        if (pendingRouteApplied && !targetStateFinalized)
        {
            await TryRestoreRouteAsync(appId, deploymentTargetId);
        }

        logger.Error(deploymentException,
            "Generic package deployment failed for AppId={AppId}, DeploymentTargetId={DeploymentTargetId}",
            appId, deploymentTargetId);
    }

    private async Task TryMarkDeploymentFailedAsync(
        Guid appId,
        Guid deploymentTargetId,
        Guid? deploymentOperationId,
        Guid? executionAttemptId)
    {
        try
        {
            await deploymentTargetStateService.MarkCompletedAsync(
                appId,
                deploymentTargetId,
                success: false,
                cancellationToken: CancellationToken.None,
                deploymentOperationId: deploymentOperationId,
                executionAttemptId: executionAttemptId);
        }
        catch (Exception completionException)
        {
            logger.Error(completionException,
                "Failed to finalize failed generic deployment for AppId={AppId}, DeploymentTargetId={DeploymentTargetId}",
                appId, deploymentTargetId);
        }
    }

    private async Task TryRestoreRouteAsync(Guid appId, Guid deploymentTargetId)
    {
        try
        {
            await backendRouteConfigService.RefreshAsync(CancellationToken.None);
        }
        catch (Exception compensationException)
        {
            logger.Error(compensationException,
                "Failed to compensate generic route reconciliation for AppId={AppId}, DeploymentTargetId={DeploymentTargetId}",
                appId, deploymentTargetId);
        }
    }

    private async Task PersistDeployPackageSourceAsync(
        Guid appId,
        Guid ownerId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        await fileStorageService.ClearSourceAsync(ownerId, appId);
        var sourceRoot = fileStorageService.GetAppSourceDirectoryPath(ownerId, appId);
        Directory.CreateDirectory(sourceRoot);

        using var stream = file.OpenReadStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var normalizedPath = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (!normalizedPath.StartsWith("source/", StringComparison.OrdinalIgnoreCase))
                continue;

            var relativeSourcePath = normalizedPath["source/".Length..];
            if (string.IsNullOrWhiteSpace(relativeSourcePath))
                continue;

            var destinationPath = Path.GetFullPath(Path.Combine(sourceRoot, relativeSourcePath));
            var fullSourceRoot = Path.GetFullPath(sourceRoot);
            if (!destinationPath.StartsWith(fullSourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Deploy package source entry '{relativeSourcePath}' escapes the app source directory.");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var entryStream = entry.Open();
            await using var outputStream = File.Create(destinationPath);
            await entryStream.CopyToAsync(outputStream, cancellationToken);
        }
    }

    private async Task EnsureRunnerOwnershipAsync(Runner runner, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runner.ApiInstanceId))
            throw new DeploymentOperationRunnerOwnershipChangedException(runner.Id);

        var current = await dataContext.Runners.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == runner.Id, cancellationToken);
        if (current is null
            || !string.Equals(current.ApiInstanceId, apiInstanceIdentityService.ApiInstanceId, StringComparison.Ordinal)
            || !string.Equals(current.ConnectionId, runner.ConnectionId, StringComparison.Ordinal))
        {
            throw new DeploymentOperationRunnerOwnershipChangedException(runner.Id);
        }
    }

    private async Task<FileUploadCompleteMessage> UploadPackageToRunnerAsync(
        Guid appId,
        Runner runner,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        const int chunkSize = 256 * 1024;
        var (sessionId, completionTask) = uploadRegistry.Register(TimeSpan.FromMinutes(10), cancellationToken);
        using var stream = file.OpenReadStream();
        var buffer = new byte[chunkSize];
        var chunkIndex = 0;
        try
        {
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await EnsureRunnerOwnershipAsync(runner, cancellationToken);
                var chunk = new FileUploadChunk
                {
                    SessionId = sessionId,
                    ChunkIndex = chunkIndex++,
                    Data = bytesRead == chunkSize ? buffer : buffer[..bytesRead],
                    IsLast = stream.Position >= stream.Length,
                    FileName = $"{appId:N}.zip",
                    ContentType = "application/zip",
                    AppId = appId
                };
                await automatonHub.Clients.Client(runner.ConnectionId!)
                    .SendAsync(AutomatonHubMethods.ReceiveFileChunk, chunk, cancellationToken);
            }
            while (true)
            {
                var completed = await Task.WhenAny(completionTask, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken));
                if (completed == completionTask)
                    return await completionTask;
                await EnsureRunnerOwnershipAsync(runner, cancellationToken);
            }
        }
        finally
        {
            uploadRegistry.TryCancel(sessionId);
        }
    }

    private static void ValidateDeploymentTargetResponse(Guid expected, Guid actual)
    {
        if (expected != actual)
            throw new InvalidOperationException("Runner deployment response did not match the selected deployment target.");
    }
}
