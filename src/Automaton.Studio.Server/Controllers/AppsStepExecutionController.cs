using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Constants;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Automaton.Studio.Server.Controllers;

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
            var inputs = request.Inputs != null
                ? new Dictionary<string, string>(request.Inputs, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ApplyTestRunIdInput(inputs, request.TestRunId);

            var result = await stepExecutionService.ExecuteStepForAppAsync(
                app,
                stepIdentifier,
                inputs,
                request.RunnerNames,
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
            var inputs = request.Inputs != null
                ? new Dictionary<string, string>(request.Inputs, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ApplyTestRunIdInput(inputs, request.TestRunId);

            var result = await stepExecutionService.ExecuteStepForAppAsync(
                app,
                stepIdentifier,
                inputs,
                request.RunnerNames,
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
        CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true ||
            (User.HasClaim(c => c.Type == PrivateAppAuthConstants.ScopeClaim)
             && !privateAppLaunchService.IsScopedPrivateAppPrincipal(User, appId)))
        {
            return Task.FromResult<ActionResult<AppStepExecutionResponse>>(Unauthorized(new { error = "Authentication required" }));
        }

        return ExecuteStepPreview(appId, stepIdentifier, request, cancellationToken);
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

    private void ApplyTestRunIdInput(Dictionary<string, string> inputs, string? bodyTestRunId)
    {
        var testRunId = ResolveTestRunId(bodyTestRunId);
        if (string.IsNullOrWhiteSpace(testRunId))
            return;

        inputs[AppExecutionContextKeys.TestRunIdInput] = testRunId;
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
