using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Automaton.Api.Constants;
using Automaton.Api.Models;
using Automaton.Api.Services;

namespace Automaton.Api.Controllers;

/// <summary>
/// Controller for executing Custom Steps directly from published Web Apps
/// </summary>
[ApiController]
[Route("api/web-apps")]
public class AppsStepExecutionController : ControllerBase
{
    private readonly AppsService appsService;
    private readonly AppsStepExecutionService stepExecutionService;
    private readonly Serilog.ILogger logger;
    private readonly PrivateAppLaunchService privateAppLaunchService;

    public AppsStepExecutionController(
        AppsService appsService,
        CustomStepsService customStepsService,
        AppsStepExecutionService stepExecutionService,
        PrivateAppLaunchService privateAppLaunchService)
    {
        this.appsService = appsService;
        this.stepExecutionService = stepExecutionService;
        this.privateAppLaunchService = privateAppLaunchService;
        this.logger = Serilog.Log.ForContext<AppsStepExecutionController>();
    }

    /// <summary>
    /// Execute a Custom Step from a published Web App (by slug)
    /// </summary>
    /// <param name="appSlug">The app's public URL slug</param>
    /// <param name="stepIdentifier">Step name or GUID</param>
    /// <param name="request">Execution request with inputs and runner selection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost("{appSlug}/steps/{stepIdentifier}/execute")]
    [AllowAnonymous] // Auth is checked internally based on app settings
    [EnableRateLimiting(RateLimitPolicyNames.PublicAppStepExecution)]
    public async Task<ActionResult<AppStepExecutionResponse>> ExecuteStep(
        string appSlug,
        string stepIdentifier,
        [FromBody] AppStepExecutionRequest request,
        [FromHeader(Name = AppExecutionContextKeys.TestRunIdHeader)] string[]? testRunIdHeaders,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.Debug(
                "Step execution request: App={AppSlug}, Step={StepIdentifier}", 
                appSlug, stepIdentifier);

            // 1. Validate app exists and is published
            var app = await appsService.GetPublishedAppInfoAsync(appSlug, cancellationToken);
            
            if (app == null)
            {
                logger.Warning("App not found: {AppSlug}", appSlug);
                return NotFound(new { error = "App not found" });
            }

            if (app.OwnerId == null)
            {
                logger.Error("App has no owner: {AppSlug}", appSlug);
                return StatusCode(500, new { error = "App configuration error: no owner" });
            }

            // 2. Check authentication requirement
            if (app.RequiresAuthentication && User.Identity?.IsAuthenticated != true)
            {
                logger.Warning("Authentication required for app: {AppSlug}", appSlug);
                return Unauthorized(new { error = "Authentication required" });
            }

            // 3. Execute the step
            var result = await stepExecutionService.ExecuteStepForAppAsync(
                app,
                stepIdentifier,
                request,
                testRunIdHeaders?.FirstOrDefault(),
                cancellationToken);

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.Warning(ex, "Unauthorized step execution: {StepIdentifier}", stepIdentifier);
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            logger.Warning(ex, "Invalid step execution: {StepIdentifier}", stepIdentifier);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Step execution failed: App={AppSlug}, Step={StepIdentifier}", 
                appSlug, stepIdentifier);
            return StatusCode(500, new { error = "Internal server error during step execution" });
        }
    }

    /// <summary>
    /// Execute a Custom Step in preview mode (by app ID, requires authentication).
    /// This allows testing backend steps before publishing the app.
    /// </summary>
    /// <param name="appId">The app's GUID</param>
    /// <param name="stepIdentifier">Step name or GUID</param>
    /// <param name="request">Execution request with inputs and runner selection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost("preview/{appId:guid}/steps/{stepIdentifier}/execute")]
    [Authorize] // Preview mode always requires authentication
    public async Task<ActionResult<AppStepExecutionResponse>> ExecuteStepPreview(
        Guid appId,
        string stepIdentifier,
        [FromBody] AppStepExecutionRequest request,
        [FromHeader(Name = AppExecutionContextKeys.TestRunIdHeader)] string[]? testRunIdHeaders,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.Debug(
                "Preview step execution request: AppId={AppId}, Step={StepIdentifier}", 
                appId, stepIdentifier);

            // 1. Get app by ID (doesn't need to be published for preview)
            var app = await appsService.GetAccessibleAppInfoByIdAsync(appId, cancellationToken);
            
            if (app == null)
            {
                logger.Warning("App not found: {AppId}", appId);
                return NotFound(new { error = "App not found" });
            }

            if (app.OwnerId == null)
            {
                logger.Error("App has no owner: {AppId}", appId);
                return StatusCode(500, new { error = "App configuration error: no owner" });
            }

            // 2. Execute the step
            var result = await stepExecutionService.ExecuteStepForAppAsync(
                app,
                stepIdentifier,
                request,
                testRunIdHeaders?.FirstOrDefault(),
                cancellationToken);

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.Warning(ex, "Unauthorized preview step execution: {StepIdentifier}", stepIdentifier);
            return Unauthorized(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            logger.Warning(ex, "Invalid preview step execution: {StepIdentifier}", stepIdentifier);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Preview step execution failed: AppId={AppId}, Step={StepIdentifier}", 
                appId, stepIdentifier);
            return StatusCode(500, new { error = "Internal server error during step execution" });
        }
    }

    /// <summary>
    /// List available steps for an app (for discovery/debugging)
    /// </summary>
    [HttpGet("{appSlug}/steps")]
    [AllowAnonymous]
    public async Task<ActionResult<IEnumerable<AppStepInfo>>> ListSteps(string appSlug)
    {
        try
        {
            var app = await appsService.GetPublishedAppInfoAsync(appSlug);
            
            if (app == null)
            {
                return NotFound(new { error = "App not found or not published" });
            }

            if (app.RequiresAuthentication && User.Identity?.IsAuthenticated != true)
            {
                return Unauthorized(new { error = "Authentication required" });
            }

            var steps = await stepExecutionService.GetLinkedStepsAsync(app.Id);
            
            return Ok(steps);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to list steps for app: {AppSlug}", appSlug);
            return StatusCode(500, new { error = "Failed to list steps" });
        }
    }

    /// <summary>
    /// List available steps for an app in preview mode (by app ID)
    /// </summary>
    [HttpGet("preview/{appId:guid}/steps")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<AppStepInfo>>> ListStepsPreview(Guid appId)
    {
        try
        {
            var app = await appsService.GetAccessibleAppInfoByIdAsync(appId);
            
            if (app == null)
            {
                return NotFound(new { error = "App not found" });
            }

            var steps = await stepExecutionService.GetLinkedStepsAsync(app.Id);
            
            return Ok(steps);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to list steps for app preview: {AppId}", appId);
            return StatusCode(500, new { error = "Failed to list steps" });
        }
    }

    [HttpPost("app/{appId:guid}/steps/{stepIdentifier}/execute")]
    [Authorize]
    public Task<ActionResult<AppStepExecutionResponse>> ExecuteStepPrivate(
        Guid appId,
        string stepIdentifier,
        [FromBody] AppStepExecutionRequest request,
        [FromHeader(Name = AppExecutionContextKeys.TestRunIdHeader)] string[]? testRunIdHeaders,
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true ||
            (User.HasClaim(c => c.Type == PrivateAppAuthConstants.ScopeClaim)
             && !privateAppLaunchService.IsScopedPrivateAppPrincipal(User, appId)))
        {
            return Task.FromResult<ActionResult<AppStepExecutionResponse>>(Unauthorized(new { error = "Authentication required" }));
        }

        return ExecuteStepPreview(appId, stepIdentifier, request, testRunIdHeaders, cancellationToken);
    }

    [HttpGet("app/{appId:guid}/steps")]
    [Authorize]
    public Task<ActionResult<IEnumerable<AppStepInfo>>> ListStepsPrivate(Guid appId)
    {
        if (User.Identity?.IsAuthenticated != true ||
            (User.HasClaim(c => c.Type == PrivateAppAuthConstants.ScopeClaim)
             && !privateAppLaunchService.IsScopedPrivateAppPrincipal(User, appId)))
        {
            return Task.FromResult<ActionResult<IEnumerable<AppStepInfo>>>(Unauthorized(new { error = "Authentication required" }));
        }

        return ListStepsPreview(appId);
    }

}
