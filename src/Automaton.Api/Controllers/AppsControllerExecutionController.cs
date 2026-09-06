using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Automaton.Api.Services;
using Automaton.Api.Constants;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;

namespace Automaton.Api.Controllers;

/// <summary>
/// Endpoints for deploying and executing AppController backend scripts.
/// </summary>
[ApiController]
public class AppsControllerExecutionController : ControllerBase
{
    private readonly AppsService appsService;
    private readonly AppsControllerExecutionService controllerExecutionService;
    private readonly UserContextService userContextService;
    private readonly ConfirmedEmailGuardService confirmedEmailGuardService;
    private readonly Serilog.ILogger logger;
    private readonly IAppDeployCapabilityService appDeployCapabilityService;
    private readonly IApplicationEventService applicationEventService;
    private readonly ManagedRunnerBootstrapService managedRunnerBootstrapService;
    private readonly DeploymentOperationService deploymentOperationService;

    public AppsControllerExecutionController(
        AppsService appsService,
        AppsControllerExecutionService controllerExecutionService,
        UserContextService userContextService,
        ConfirmedEmailGuardService confirmedEmailGuardService,
        IAppDeployCapabilityService appDeployCapabilityService,
        IApplicationEventService applicationEventService,
        ManagedRunnerBootstrapService managedRunnerBootstrapService,
        DeploymentOperationService deploymentOperationService)
    {
        this.appsService = appsService;
        this.controllerExecutionService = controllerExecutionService;
        this.userContextService = userContextService;
        this.confirmedEmailGuardService = confirmedEmailGuardService;
        this.appDeployCapabilityService = appDeployCapabilityService;
        this.applicationEventService = applicationEventService;
        this.managedRunnerBootstrapService = managedRunnerBootstrapService;
        this.deploymentOperationService = deploymentOperationService;
        this.logger = Serilog.Log.ForContext<AppsControllerExecutionController>();
    }

    [HttpPost("api/apps/{appId:guid}/managed-runtime/readiness")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<ManagedRunnerBootstrapResult>> GetManagedRuntimeReadiness(
        Guid appId,
        CancellationToken cancellationToken)
    {
        return Ok(await managedRunnerBootstrapService.EnsureRunnerAssignmentAsync(
            appId,
            userContextService.GetUserId(),
            cancellationToken));
    }

    /// <summary>
    /// Returns a lightweight pre-deploy summary: script count and whether a requirements.txt is present.
    /// Called before deploying so the frontend can warn the user about pending package installation.
    /// </summary>
    [HttpGet("api/apps/{appId:guid}/backend/deploy-info")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<DeployInfoResponse>> GetDeployInfo(
        Guid appId,
        CancellationToken cancellationToken)
    {
        var userId = userContextService.GetUserId();
        var result = await controllerExecutionService.GetDeployInfoAsync(appId, userId);
        return Ok(result);
    }

    [HttpGet("api/apps/{appId:guid}/deploy-capabilities")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<AppDeployCapabilitiesResponse>> GetDeployCapabilities(
        Guid appId,
        CancellationToken cancellationToken)
    {
        var appInfo = await appsService.GetAccessibleAppInfoByIdAsync(appId, cancellationToken);
        if (appInfo == null)
        {
            return NotFound(new { message = "App not found" });
        }

        var ownerId = appInfo.OwnerId ?? userContextService.GetUserId();
        var result = await appDeployCapabilityService.GetCapabilitiesAsync(ownerId, appId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Deploy all backend scripts for an app to its configured runner(s).
    /// Called from the app editor "Deploy Backend" button.
    /// </summary>
    [HttpPost("api/apps/{appId:guid}/backend/deploy")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<DeployBackendResponse>> DeployBackend(
        Guid appId,
        [FromBody] DeployBackendRequest? request,
        CancellationToken cancellationToken)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            var userId = userContextService.GetUserId();
            logger.Debug("Deploy backend request: AppId={AppId}, UserId={UserId}", appId, userId);
            await WriteApplicationEventAsync(
                appId,
                ApplicationEventLevels.Info,
                ApplicationEventCategories.Deployment,
                ApplicationEventTypes.BackendDeployStarted,
                "Backend deployment started.",
                new { request?.RunnerNames },
                userId,
                cancellationToken);

            var result = await controllerExecutionService.DeployBackendToRunnersAsync(
                appId, userId, request?.TargetSlug, request?.RunnerNames, cancellationToken);

            await WriteApplicationEventAsync(
                appId,
                result.Success ? ApplicationEventLevels.Info : ApplicationEventLevels.Warning,
                ApplicationEventCategories.Deployment,
                result.Success ? ApplicationEventTypes.BackendDeployCompleted : ApplicationEventTypes.BackendDeployFailed,
                result.Success ? "Backend deployment completed." : "Backend deployment completed with issues.",
                new
                {
                    result.DeployedCount,
                    result.TotalScripts,
                    result.RunnerCount,
                    result.HasRequirements,
                    result.Message
                },
                userId,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Deploy backend failed: AppId={AppId}", appId);
            await WriteApplicationEventAsync(
                appId,
                ApplicationEventLevels.Error,
                ApplicationEventCategories.Deployment,
                ApplicationEventTypes.BackendDeployFailed,
                "Backend deployment failed.",
                new { Error = ex.Message },
                userContextService.GetUserId(),
                cancellationToken);
            return StatusCode(500, new { error = "Deploy failed: " + ex.Message });
        }
    }

    [HttpPost("api/apps/{appId:guid}/deploy-package")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<DeploymentOperationResponse>> DeployPackage(
        Guid appId,
        [FromForm] IFormFile? file,
        [FromQuery] bool clean = false,
        [FromQuery] string? targetSlug = null,
        CancellationToken cancellationToken = default)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            var userId = userContextService.GetUserId();
            var result = await deploymentOperationService.AcceptAsync(
                appId, userId, targetSlug, file, clean, cancellationToken);
            return AcceptedAtAction(
                nameof(GetDeploymentOperation),
                new { appId, operationId = result.OperationId },
                result);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Deploy package failed: AppId={AppId}", appId);
            return StatusCode(500, new { error = "Deploy package failed: " + ex.Message });
        }
    }

    [HttpGet("api/apps/{appId:guid}/deploy-operations/{operationId:guid}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<DeploymentOperationResponse>> GetDeploymentOperation(
        Guid appId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var result = await deploymentOperationService.GetAsync(
            appId,
            userContextService.GetUserId(),
            operationId,
            cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("api/apps/{appId:guid}/deploy")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<DeployAppResponse>> DeployApp(
        Guid appId,
        [FromQuery] string? targetSlug,
        CancellationToken cancellationToken)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            var userId = userContextService.GetUserId();
            logger.Debug("Deploy app request: AppId={AppId}, UserId={UserId}", appId, userId);
            await WriteApplicationEventAsync(
                appId,
                ApplicationEventLevels.Info,
                ApplicationEventCategories.Deployment,
                ApplicationEventTypes.AppDeployStarted,
                "Application deployment started.",
                null,
                userId,
                cancellationToken);

            var result = await controllerExecutionService.DeployStudioAppAsync(
                appId, userId, targetSlug, cancellationToken);
            await WriteApplicationEventAsync(
                appId,
                result.Success ? ApplicationEventLevels.Info : ApplicationEventLevels.Warning,
                ApplicationEventCategories.Deployment,
                result.Success ? ApplicationEventTypes.AppDeployCompleted : ApplicationEventTypes.AppDeployFailed,
                result.Success ? "Application deployment completed." : "Application deployment completed with issues.",
                new
                {
                    result.FrontendFilesSynced,
                    result.BackendFilesSynced,
                    result.FrontendAction,
                    result.BackendAction,
                    result.Message,
                    BackendDeploy = result.BackendDeploy == null ? null : new
                    {
                        result.BackendDeploy.Success,
                        result.BackendDeploy.DeployedCount,
                        result.BackendDeploy.TotalScripts,
                        result.BackendDeploy.RunnerCount,
                        result.BackendDeploy.Message
                    }
                },
                userId,
                cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Deploy app failed: AppId={AppId}", appId);
            await WriteApplicationEventAsync(
                appId,
                ApplicationEventLevels.Error,
                ApplicationEventCategories.Deployment,
                ApplicationEventTypes.AppDeployFailed,
                "Application deployment failed.",
                new { Error = ex.Message },
                userContextService.GetUserId(),
                cancellationToken);
            return StatusCode(500, new { error = "Deploy app failed: " + ex.Message });
        }
    }

    [HttpPost("api/apps/{appId:guid}/frontend/deploy")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<DeployAppResponse>> DeployFrontend(
        Guid appId,
        [FromQuery] string? targetSlug,
        CancellationToken cancellationToken)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            var userId = userContextService.GetUserId();
            logger.Debug("Deploy frontend request: AppId={AppId}, UserId={UserId}", appId, userId);
            await WriteApplicationEventAsync(
                appId,
                ApplicationEventLevels.Info,
                ApplicationEventCategories.Deployment,
                ApplicationEventTypes.FrontendDeployStarted,
                "Frontend deployment started.",
                null,
                userId,
                CancellationToken.None);

            var result = await controllerExecutionService.DeployFrontendAsync(
                appId, userId, targetSlug, cancellationToken);
            await WriteApplicationEventAsync(
                appId,
                result.Success ? ApplicationEventLevels.Info : ApplicationEventLevels.Warning,
                ApplicationEventCategories.Deployment,
                result.Success ? ApplicationEventTypes.FrontendDeployCompleted : ApplicationEventTypes.FrontendDeployFailed,
                result.Success ? "Frontend deployment completed." : "Frontend deployment completed with issues.",
                new
                {
                    result.FrontendFilesSynced,
                    result.FrontendAction,
                    result.Message
                },
                userId,
                CancellationToken.None);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Deploy frontend failed: AppId={AppId}", appId);
            await WriteApplicationEventAsync(
                appId,
                ApplicationEventLevels.Error,
                ApplicationEventCategories.Deployment,
                ApplicationEventTypes.FrontendDeployFailed,
                "Frontend deployment failed.",
                new { Error = ex.Message },
                userContextService.GetUserId(),
                CancellationToken.None);
            return StatusCode(500, new { error = "Deploy frontend failed: " + ex.Message });
        }
    }

    [HttpPost("api/apps/{appId:guid}/backend/redeploy")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<DeployAppResponse>> DeployBackendComponent(
        Guid appId,
        [FromQuery] string? targetSlug,
        CancellationToken cancellationToken)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            var userId = userContextService.GetUserId();
            logger.Debug("Deploy backend component request: AppId={AppId}, UserId={UserId}", appId, userId);
            await WriteApplicationEventAsync(
                appId,
                ApplicationEventLevels.Info,
                ApplicationEventCategories.Deployment,
                ApplicationEventTypes.BackendDeployStarted,
                "Backend deployment started.",
                null,
                userId,
                cancellationToken);

            var result = await controllerExecutionService.DeployBackendComponentAsync(
                appId, userId, targetSlug, cancellationToken);
            await WriteApplicationEventAsync(
                appId,
                result.Success ? ApplicationEventLevels.Info : ApplicationEventLevels.Warning,
                ApplicationEventCategories.Deployment,
                result.Success ? ApplicationEventTypes.BackendDeployCompleted : ApplicationEventTypes.BackendDeployFailed,
                result.Success ? "Backend deployment completed." : "Backend deployment completed with issues.",
                new
                {
                    result.BackendFilesSynced,
                    result.BackendAction,
                    result.Message,
                    BackendDeploy = result.BackendDeploy == null ? null : new
                    {
                        result.BackendDeploy.Success,
                        result.BackendDeploy.DeployedCount,
                        result.BackendDeploy.TotalScripts,
                        result.BackendDeploy.RunnerCount,
                        result.BackendDeploy.Message
                    }
                },
                userId,
                cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Deploy backend component failed: AppId={AppId}", appId);
            await WriteApplicationEventAsync(
                appId,
                ApplicationEventLevels.Error,
                ApplicationEventCategories.Deployment,
                ApplicationEventTypes.BackendDeployFailed,
                "Backend deployment failed.",
                new { Error = ex.Message },
                userContextService.GetUserId(),
                cancellationToken);
            return StatusCode(500, new { error = "Deploy backend component failed: " + ex.Message });
        }
    }

    private Task WriteApplicationEventAsync(
        Guid appId,
        string level,
        string category,
        string eventType,
        string message,
        object? metadata,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        return applicationEventService.WriteAsync(new ApplicationEventWriteRequest
        {
            AppId = appId,
            Level = level,
            Category = category,
            EventType = eventType,
            Message = message,
            Metadata = metadata,
            ActorUserId = actorUserId
        }, cancellationToken);
    }

    /// <summary>
    /// Legacy controller-action execution is retired. Use the canonical Automaton Action API.
    /// This compatibility route remains only as an explicit Stage 2C deletion boundary.
    /// </summary>
    [HttpPost("api/web-apps/{appSlug}/controllers/{controllerName}/actions/{actionName}/execute")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.PublicAppControllerExecution)]
    public ActionResult<AppControllerActionResponse> ExecuteAction(
        string appSlug,
        string controllerName,
        string actionName,
        [FromBody] AppControllerActionRequest request,
        CancellationToken cancellationToken)
    {
        return RetiredControllerActionResponse();
    }

    /// <summary>
    /// Legacy controller-action preview is retired. This is a Stage 2C deletion boundary.
    /// </summary>
    [HttpPost("api/web-apps/preview/{appId:guid}/controllers/{controllerName}/actions/{actionName}/execute")]
    [Authorize]
    public ActionResult<AppControllerActionResponse> ExecuteActionPreview(
        Guid appId,
        string controllerName,
        string actionName,
        [FromBody] AppControllerActionRequest request,
        CancellationToken cancellationToken)
    {
        return RetiredControllerActionResponse();
    }

    /// <summary>
    /// Legacy private controller-action execution is retired. This is a Stage 2C deletion boundary.
    /// </summary>
    [HttpPost("api/web-apps/app/{appId:guid}/controllers/{controllerName}/actions/{actionName}/execute")]
    [Authorize]
    public ActionResult<AppControllerActionResponse> ExecuteActionPrivate(
        Guid appId,
        string controllerName,
        string actionName,
        [FromBody] AppControllerActionRequest request,
        CancellationToken cancellationToken)
    {
        return RetiredControllerActionResponse();
    }

    private ActionResult<AppControllerActionResponse> RetiredControllerActionResponse() =>
        StatusCode(StatusCodes.Status410Gone, new
        {
            error = "Legacy controller action execution is retired. Use the canonical Automaton Action API."
        });
}
