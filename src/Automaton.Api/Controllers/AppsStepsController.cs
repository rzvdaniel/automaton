using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/apps")]
public sealed class AppsStepsController(AppStepsService appStepsService, ILogger<AppsStepsController> logger) : ControllerBase
{
    [HttpGet("{id}/steps")]
    public async Task<ActionResult<IEnumerable<LinkedStepInfo>>> GetLinkedSteps(Guid id)
    {
        try
        {
            return Ok(await appStepsService.GetLinkedStepsAsync(id));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error getting linked steps for app {AppId}", id);
            return StatusCode(500, new { message = "Failed to get linked steps" });
        }
    }

    [HttpGet("{id}/steps/available")]
    public async Task<ActionResult<IEnumerable<LinkedStepInfo>>> GetAvailableSteps(Guid id)
    {
        try
        {
            return Ok(await appStepsService.GetAvailableStepsAsync(id));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error getting available steps for app {AppId}", id);
            return StatusCode(500, new { message = "Failed to get available steps" });
        }
    }

    [HttpPost("{appId}/steps/{stepId}/link")]
    public async Task<IActionResult> LinkStep(Guid appId, Guid stepId)
    {
        try
        {
            await appStepsService.LinkStepAsync(appId, stepId);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error linking step {StepId} to app {AppId}", stepId, appId);
            return StatusCode(500, new { message = "Failed to link step" });
        }
    }

    [HttpPost("{appId}/steps/link-multiple")]
    public async Task<IActionResult> LinkMultipleSteps(Guid appId, [FromBody] LinkMultipleStepsRequest request)
    {
        try
        {
            if (request.StepIds is null || !request.StepIds.Any())
            {
                return BadRequest(new { message = "No step IDs provided" });
            }

            await appStepsService.LinkStepsAsync(appId, request.StepIds);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error linking multiple steps to app {AppId}", appId);
            return StatusCode(500, new { message = "Failed to link steps" });
        }
    }

    [HttpPost("{appId}/steps/create")]
    public async Task<ActionResult<LinkedStepInfo>> CreateStepForApp(Guid appId, [FromBody] CreateAppStepRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
        {
            return BadRequest(new { message = "Step name is required" });
        }

        try
        {
            var step = await appStepsService.CreateStepForAppAsync(appId, request.Name);
            return CreatedAtAction(nameof(GetLinkedSteps), new { id = appId }, step);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error creating step for app {AppId}", appId);
            return StatusCode(500, new { message = "Failed to create step" });
        }
    }

    [HttpDelete("{appId}/steps/{stepId}")]
    public async Task<IActionResult> UnlinkStep(Guid appId, Guid stepId)
    {
        try
        {
            await appStepsService.UnlinkStepAsync(appId, stepId);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error unlinking step {StepId} from app {AppId}", stepId, appId);
            return StatusCode(500, new { message = "Failed to unlink step" });
        }
    }
}
