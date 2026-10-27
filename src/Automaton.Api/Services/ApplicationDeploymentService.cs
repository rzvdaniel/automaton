using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Constants;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Services;

/// <summary>
/// Owns the generic application package deployment path.
/// Controller and Action compatibility paths deliberately live elsewhere.
/// </summary>
public sealed class ApplicationDeploymentService
{
    private readonly DeploymentPackageRunnerService packageRunnerService;
    private readonly ManagedRunnerBootstrapService managedRunnerBootstrapService;
    private readonly AppRunnerAssignmentPolicy runnerAssignmentPolicy;
    private readonly IAppDeploymentTargetResolver deploymentTargetResolver;
    private readonly AppDeploymentTargetStateService deploymentTargetStateService;
    private readonly DeploymentPackageFinalizationService packageFinalizationService;
    private readonly Serilog.ILogger logger;

    public ApplicationDeploymentService(
        DeploymentPackageRunnerService packageRunnerService,
        ManagedRunnerBootstrapService managedRunnerBootstrapService,
        AppRunnerAssignmentPolicy runnerAssignmentPolicy,
        IAppDeploymentTargetResolver deploymentTargetResolver,
        AppDeploymentTargetStateService deploymentTargetStateService,
        DeploymentPackageFinalizationService packageFinalizationService)
    {
        this.packageRunnerService = packageRunnerService;
        this.managedRunnerBootstrapService = managedRunnerBootstrapService;
        this.runnerAssignmentPolicy = runnerAssignmentPolicy;
        this.deploymentTargetResolver = deploymentTargetResolver;
        this.deploymentTargetStateService = deploymentTargetStateService;
        this.packageFinalizationService = packageFinalizationService;
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
            var runner = await runnerAssignmentPolicy.GetAssignedRunnerForDurableManagementAsync(
                appId,
                ownerId,
                targetSlug: target.Slug,
                cancellationToken: cancellationToken);
            if (runner is null)
                throw new InvalidOperationException("Assign a Management Runner to this app before deploying.");

            var result = await packageRunnerService.DeployAsync(
                appId,
                ownerId,
                target.Id,
                runner,
                file,
                packageApplication,
                clean,
                deploymentOperationId,
                executionAttemptId,
                cancellationToken);

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
                await packageFinalizationService.FinalizeAsync(
                    appId,
                    target.Id,
                    ownerId,
                    runner,
                    file,
                    packageApplication,
                    cancellationToken,
                    deploymentOperationId);
                pendingRouteApplied = true;

                await deploymentTargetStateService.MarkCompletedAsync(
                    appId,
                    target.Id,
                    success: true,
                    cancellationToken: CancellationToken.None,
                    genericRuntimeDeployed: packageApplication.Runtime.Command is not null
                        || packageApplication.Compose is not null || packageApplication.IsStaticSite,
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
            await packageFinalizationService.RestoreRoutesAsync();
        }
        catch (Exception compensationException)
        {
            logger.Error(compensationException,
                "Failed to compensate generic route reconciliation for AppId={AppId}, DeploymentTargetId={DeploymentTargetId}",
                appId, deploymentTargetId);
        }
    }

    private static void ValidateDeploymentTargetResponse(Guid expected, Guid actual)
    {
        if (expected != actual)
            throw new InvalidOperationException("Runner deployment response did not match the selected deployment target.");
    }
}
