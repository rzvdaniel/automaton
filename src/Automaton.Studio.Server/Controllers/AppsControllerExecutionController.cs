using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text.Json;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Endpoints for deploying and executing AppController backend scripts.
/// </summary>
[ApiController]
public class AppsControllerExecutionController : ControllerBase
{
    private readonly AppsService appsService;
    private readonly AppsControllerExecutionService controllerExecutionService;
    private readonly UserContextService userContextService;
    private readonly IAppExecutionLogService executionLogService;
    private readonly Serilog.ILogger logger;

    public AppsControllerExecutionController(
        AppsService appsService,
        AppsControllerExecutionService controllerExecutionService,
        UserContextService userContextService,
        IAppExecutionLogService executionLogService)
    {
        this.appsService = appsService;
        this.controllerExecutionService = controllerExecutionService;
        this.userContextService = userContextService;
        this.executionLogService = executionLogService;
        this.logger = Serilog.Log.ForContext<AppsControllerExecutionController>();
    }

    /// <summary>
    /// Deploy all backend scripts for an app to its configured runner(s).
    /// Called from the app editor "Deploy Backend" button.
    /// </summary>
    [HttpPost("api/apps/{appId:guid}/backend/deploy")]
    [Authorize]
    public async Task<ActionResult<DeployBackendResponse>> DeployBackend(
        Guid appId,
        [FromBody] DeployBackendRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = userContextService.GetUserId();
            logger.Information("Deploy backend request: AppId={AppId}, UserId={UserId}", appId, userId);

            var result = await controllerExecutionService.DeployBackendToRunnersAsync(
                appId, userId, request?.RunnerNames, cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Deploy backend failed: AppId={AppId}", appId);
            return StatusCode(500, new { error = "Deploy failed: " + ex.Message });
        }
    }

    /// <summary>
    /// Execute a controller action from a published app (by slug).
    /// </summary>
    [HttpPost("api/web-apps/{appSlug}/controllers/{controllerName}/actions/{actionName}/execute")]
    [AllowAnonymous]
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
            logger.Information(
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

            var result = await controllerExecutionService.ExecuteActionAsync(
                app,
                controllerName,
                actionName,
                request.Inputs ?? new Dictionary<string, string>(),
                request.RunnerNames,
                cancellationToken);

            sw.Stop();

            _ = executionLogService.LogExecutionAsync(new AppExecutionLogRequest
            {
                AppId = app.Id,
                ControllerName = controllerName,
                ActionName = actionName,
                RequestPayload = request.Inputs != null ? JsonSerializer.Serialize(request.Inputs) : null,
                ResponsePayload = result != null ? JsonSerializer.Serialize(result) : null,
                Success = result.Success,
                ErrorMessage = result.Error,
                DurationMs = sw.ElapsedMilliseconds,
                UserId = User.Identity?.IsAuthenticated == true ? userContextService.GetUserId() : (Guid?)null,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            sw.Stop();
            logger.Warning(ex, "Invalid controller action: {ControllerName}.{ActionName}", controllerName, actionName);

            _ = LogFailedExecutionAsync(appSlug, controllerName, actionName, request, sw.ElapsedMilliseconds, ex.Message);

            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.Error(ex, "Controller action failed: App={AppSlug}, Controller={ControllerName}, Action={ActionName}",
                appSlug, controllerName, actionName);

            _ = LogFailedExecutionAsync(appSlug, controllerName, actionName, request, sw.ElapsedMilliseconds, ex.Message);

            return StatusCode(500, new { error = "Internal server error during controller action execution" });
        }
    }

    private async Task LogFailedExecutionAsync(
        string appSlug, string controllerName, string actionName,
        AppControllerActionRequest request, long durationMs, string errorMessage)
    {
        try
        {
            var app = await appsService.GetPublishedAppInfoAsync(appSlug, CancellationToken.None);
            if (app == null) return;

            await executionLogService.LogExecutionAsync(new AppExecutionLogRequest
            {
                AppId = app.Id,
                ControllerName = controllerName,
                ActionName = actionName,
                RequestPayload = request.Inputs != null ? JsonSerializer.Serialize(request.Inputs) : null,
                Success = false,
                ErrorMessage = errorMessage,
                DurationMs = durationMs,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            });
        }
        catch
        {
            // Best-effort; don't rethrow
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
            logger.Information(
                "Preview controller action request: AppId={AppId}, Controller={ControllerName}, Action={ActionName}",
                appId, controllerName, actionName);

            var app = await appsService.GetAppInfoByIdAsync(appId, cancellationToken);

            if (app == null)
            {
                return NotFound(new { error = "App not found" });
            }

            if (app.OwnerId == null)
            {
                return StatusCode(500, new { error = "App configuration error: no owner" });
            }

            var result = await controllerExecutionService.ExecuteActionAsync(
                app,
                controllerName,
                actionName,
                request.Inputs ?? new Dictionary<string, string>(),
                request.RunnerNames,
                cancellationToken);

            sw.Stop();

            _ = executionLogService.LogExecutionAsync(new AppExecutionLogRequest
            {
                AppId = appId,
                ControllerName = controllerName,
                ActionName = actionName,
                RequestPayload = request.Inputs != null ? JsonSerializer.Serialize(request.Inputs) : null,
                ResponsePayload = result != null ? JsonSerializer.Serialize(result) : null,
                Success = result.Success,
                ErrorMessage = result.Error,
                DurationMs = sw.ElapsedMilliseconds,
                UserId = userContextService.GetUserId(),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            sw.Stop();
            logger.Warning(ex, "Invalid preview controller action: {ControllerName}.{ActionName}", controllerName, actionName);

            _ = LogFailedPreviewAsync(appId, controllerName, actionName, request, sw.ElapsedMilliseconds, ex.Message);

            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.Error(ex, "Preview controller action failed: AppId={AppId}, Controller={ControllerName}, Action={ActionName}",
                appId, controllerName, actionName);

            _ = LogFailedPreviewAsync(appId, controllerName, actionName, request, sw.ElapsedMilliseconds, ex.Message);

            return StatusCode(500, new { error = "Internal server error during controller action execution" });
        }
    }

    private async Task LogFailedPreviewAsync(
        Guid appId, string controllerName, string actionName,
        AppControllerActionRequest request, long durationMs, string errorMessage)
    {
        try
        {
            await executionLogService.LogExecutionAsync(new AppExecutionLogRequest
            {
                AppId = appId,
                ControllerName = controllerName,
                ActionName = actionName,
                RequestPayload = request.Inputs != null ? JsonSerializer.Serialize(request.Inputs) : null,
                Success = false,
                ErrorMessage = errorMessage,
                DurationMs = durationMs,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            });
        }
        catch
        {
            // Best-effort; don't rethrow
        }
    }
}
