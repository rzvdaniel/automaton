using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Controller for executing Custom Steps directly from published Web Apps
/// </summary>
[ApiController]
[Route("api/web-apps")]
public class AppsStepExecutionController : ControllerBase
{
    private readonly AppsService appsService;
    private readonly CustomStepsService customStepsService;
    private readonly AppsStepExecutionService stepExecutionService;
    private readonly Serilog.ILogger logger;

    public AppsStepExecutionController(
        AppsService appsService,
        CustomStepsService customStepsService,
        AppsStepExecutionService stepExecutionService)
    {
        this.appsService = appsService;
        this.customStepsService = customStepsService;
        this.stepExecutionService = stepExecutionService;
        this.logger = Serilog.Log.ForContext<AppsStepExecutionController>();
    }

    /// <summary>
    /// Execute a Custom Step from a published Web App
    /// </summary>
    /// <param name="appSlug">The app's public URL slug</param>
    /// <param name="stepIdentifier">Step name or GUID</param>
    /// <param name="request">Execution request with inputs and runner selection</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpPost("{appSlug}/steps/{stepIdentifier}/execute")]
    [AllowAnonymous] // Auth is checked internally based on app settings
    public async Task<ActionResult<AppStepExecutionResponse>> ExecuteStep(
        string appSlug,
        string stepIdentifier,
        [FromBody] AppStepExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.Information(
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
                request.Inputs ?? new Dictionary<string, string>(),
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
}
