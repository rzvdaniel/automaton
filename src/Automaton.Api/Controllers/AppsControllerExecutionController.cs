using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Diagnostics;
using System.Text.Json;
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
    private readonly PrivateAppLaunchService privateAppLaunchService;
    private readonly IAppDeployCapabilityService appDeployCapabilityService;
    private readonly IApplicationEventService applicationEventService;
    private readonly ManagedRunnerBootstrapService managedRunnerBootstrapService;

    public AppsControllerExecutionController(
        AppsService appsService,
        AppsControllerExecutionService controllerExecutionService,
        UserContextService userContextService,
        ConfirmedEmailGuardService confirmedEmailGuardService,
        PrivateAppLaunchService privateAppLaunchService,
        IAppDeployCapabilityService appDeployCapabilityService,
        IApplicationEventService applicationEventService,
        ManagedRunnerBootstrapService managedRunnerBootstrapService)
    {
        this.appsService = appsService;
        this.controllerExecutionService = controllerExecutionService;
        this.userContextService = userContextService;
        this.confirmedEmailGuardService = confirmedEmailGuardService;
        this.privateAppLaunchService = privateAppLaunchService;
        this.appDeployCapabilityService = appDeployCapabilityService;
        this.applicationEventService = applicationEventService;
        this.managedRunnerBootstrapService = managedRunnerBootstrapService;
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
                appId, userId, request?.RunnerNames, cancellationToken);

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
    public async Task<ActionResult<DeployPackageResponse>> DeployPackage(
        Guid appId,
        [FromForm] IFormFile? file,
        [FromQuery] bool clean = false,
        CancellationToken cancellationToken = default)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

        try
        {
            var userId = userContextService.GetUserId();
            var result = await controllerExecutionService.DeployPackageAsync(appId, userId, file, clean, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Deploy package failed: AppId={AppId}", appId);
            return StatusCode(500, new { error = "Deploy package failed: " + ex.Message });
        }
    }

    [HttpPost("api/apps/{appId:guid}/deploy")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<DeployAppResponse>> DeployApp(
        Guid appId,
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

            var result = await controllerExecutionService.DeployStudioAppAsync(appId, userId, cancellationToken);
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
    public async Task<ActionResult<DeployAppResponse>> DeployFrontend(Guid appId)
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

            var result = await controllerExecutionService.DeployFrontendAsync(appId, userId);
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

            var result = await controllerExecutionService.DeployBackendComponentAsync(appId, userId, cancellationToken);
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
    /// Execute a controller action from a published app (by slug).
    /// </summary>
    [HttpPost("api/web-apps/{appSlug}/controllers/{controllerName}/actions/{actionName}/execute")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.PublicAppControllerExecution)]
    public async Task<ActionResult<AppControllerActionResponse>> ExecuteAction(
        string appSlug,
        string controllerName,
        string actionName,
        [FromBody] AppControllerActionRequest request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            logger.Debug(
                "Controller action request: App={AppSlug}, Controller={ControllerName}, Action={ActionName}",
                appSlug, controllerName, actionName);

            var app = await appsService.GetPublishedAppInfoAsync(appSlug, cancellationToken);

            if (app == null)
            {
                return NotFound(new { error = "App not found" });
            }

            if (app.OwnerId == null)
            {
                return StatusCode(500, new { error = "App configuration error: no owner" });
            }

            if (app.RequiresAuthentication && User.Identity?.IsAuthenticated != true)
            {
                return Unauthorized(new { error = "Authentication required" });
            }

            var inputs = request.Inputs != null
                ? new Dictionary<string, JsonElement>(request.Inputs, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            ApplyTestRunIdInput(inputs, request.TestRunId);
            var useMockBackend = request.UseMockBackend == true;

            var result = await controllerExecutionService.ExecuteActionAsync(
                app,
                controllerName,
                actionName,
                inputs,
                request.RunnerNames,
                useMockBackend,
                cancellationToken);

            sw.Stop();

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            sw.Stop();
            logger.Warning(ex, "Invalid controller action: {ControllerName}.{ActionName}", controllerName, actionName);

            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.Error(ex, "Controller action failed: App={AppSlug}, Controller={ControllerName}, Action={ActionName}",
                appSlug, controllerName, actionName);

            return StatusCode(500, new { error = "Internal server error during controller action execution" });
        }
    }

    /// <summary>
    /// Execute a controller action in preview mode (by app ID, requires auth).
    /// </summary>
    [HttpPost("api/web-apps/preview/{appId:guid}/controllers/{controllerName}/actions/{actionName}/execute")]
    [Authorize]
    public async Task<ActionResult<AppControllerActionResponse>> ExecuteActionPreview(
        Guid appId,
        string controllerName,
        string actionName,
        [FromBody] AppControllerActionRequest request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            logger.Debug(
                "Preview controller action request: AppId={AppId}, Controller={ControllerName}, Action={ActionName}",
                appId, controllerName, actionName);

            var app = await appsService.GetAccessibleAppInfoByIdAsync(appId, cancellationToken);

            if (app == null)
            {
                return NotFound(new { error = "App not found" });
            }

            if (app.OwnerId == null)
            {
                return StatusCode(500, new { error = "App configuration error: no owner" });
            }

            var inputs = request.Inputs != null
                ? new Dictionary<string, JsonElement>(request.Inputs, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            ApplyTestRunIdInput(inputs, request.TestRunId);
            var useMockBackend = request.UseMockBackend != false;

            var result = await controllerExecutionService.ExecuteActionAsync(
                app,
                controllerName,
                actionName,
                inputs,
                request.RunnerNames,
                useMockBackend,
                cancellationToken);

            sw.Stop();

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            sw.Stop();
            logger.Warning(ex, "Invalid preview controller action: {ControllerName}.{ActionName}", controllerName, actionName);

            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.Error(ex, "Preview controller action failed: AppId={AppId}, Controller={ControllerName}, Action={ActionName}",
                appId, controllerName, actionName);

            return StatusCode(500, new { error = "Internal server error during controller action execution" });
        }
    }

    /// <summary>
    /// Execute a controller action from a private deployed app (by app ID, requires auth).
    /// </summary>
    [HttpPost("api/web-apps/app/{appId:guid}/controllers/{controllerName}/actions/{actionName}/execute")]
    [Authorize]
    public async Task<ActionResult<AppControllerActionResponse>> ExecuteActionPrivate(
        Guid appId,
        string controllerName,
        string actionName,
        [FromBody] AppControllerActionRequest request,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true ||
            (User.HasClaim(c => c.Type == PrivateAppAuthConstants.ScopeClaim)
             && !privateAppLaunchService.IsScopedPrivateAppPrincipal(User, appId)))
        {
            return Unauthorized(new { error = "Authentication required" });
        }

        var sw = Stopwatch.StartNew();

        try
        {
            logger.Debug(
                "Private deployed controller action request: AppId={AppId}, Controller={ControllerName}, Action={ActionName}",
                appId, controllerName, actionName);

            var app = await appsService.GetAccessibleAppInfoByIdAsync(appId, cancellationToken);
            if (app == null)
            {
                return NotFound(new { error = "App not found" });
            }

            if (app.OwnerId == null)
            {
                return StatusCode(500, new { error = "App configuration error: no owner" });
            }

            var inputs = request.Inputs != null
                ? new Dictionary<string, JsonElement>(request.Inputs, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            ApplyTestRunIdInput(inputs, request.TestRunId);

            var result = await controllerExecutionService.ExecuteActionAsync(
                app,
                controllerName,
                actionName,
                inputs,
                request.RunnerNames,
                useMockBackend: false,
                cancellationToken);

            sw.Stop();

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            sw.Stop();
            logger.Warning(ex, "Invalid private deployed controller action: {ControllerName}.{ActionName}", controllerName, actionName);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.Error(ex, "Private deployed controller action failed: AppId={AppId}, Controller={ControllerName}, Action={ActionName}",
                appId, controllerName, actionName);
            return StatusCode(500, new { error = "Internal server error during controller action execution" });
        }
    }

    private void ApplyTestRunIdInput(
        Dictionary<string, JsonElement> inputs,
        string? bodyTestRunId)
    {
        var testRunId = ResolveTestRunId(bodyTestRunId);
        if (string.IsNullOrWhiteSpace(testRunId))
            return;

        using var doc = JsonDocument.Parse($"\"{testRunId}\"");
        inputs[AppExecutionContextKeys.TestRunIdInput] = doc.RootElement.Clone();
    }

    private string? ResolveTestRunId(string? bodyTestRunId)
    {
        if (!string.IsNullOrWhiteSpace(bodyTestRunId))
            return bodyTestRunId.Trim();

        if (Request.Headers.TryGetValue(AppExecutionContextKeys.TestRunIdHeader, out var headerValues))
        {
            var headerValue = headerValues.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(headerValue))
                return headerValue.Trim();
        }

        return null;
    }
}
