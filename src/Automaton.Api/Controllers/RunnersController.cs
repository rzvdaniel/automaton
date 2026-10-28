using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Scripting.Engines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

public class RunnersController : BaseController
{
    private readonly RunnerService runnersService;
    public RunnersController(RunnerService runnersService)
    {
        this.runnersService = runnersService;
    }

    [HttpGet]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<IEnumerable<RunnerDetails>>> Get([FromQuery] Guid? projectId, CancellationToken cancellationToken)
    {
        return Ok(await runnersService.List(projectId, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = "AllowApiKey")]
    public ActionResult Post()
    {
        return StatusCode(StatusCodes.Status410Gone, new { message = "Install a Machine Management Runner through the one-time installation flow." });
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> Put(Guid id, RunnerDetails runner, CancellationToken cancellationToken)
    {
        await runnersService.Update(id, runner, cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken,
        [FromQuery] bool hostCleanupConfirmed = false)
    {
        try
        {
            await runnersService.Delete(id, cancellationToken, hostCleanupConfirmed);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }

        return NoContent();
    }

    [HttpPost("{id}/removal")]
    [Authorize]
    public async Task<IActionResult> PrepareRemoval(Guid id, [FromServices] RunnerRemovalService removal,
        CancellationToken cancellationToken)
    {
        try { return Ok(await removal.PrepareAsync(id, cancellationToken)); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpGet("status")]
    public async Task<ActionResult<RunnerStatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        var runners = await runnersService.List(cancellationToken);
        var onlineRunners = runners.Count(r => r.Status == RunnerStatus.Online);

        return Ok(new RunnerStatusResponse
        {
            HasOnlineRunners = onlineRunners > 0,
            TotalRunners = runners.Count(),
            OnlineRunners = onlineRunners
        });
    }

    /// <summary>
    /// Get script engine settings for a specific runner
    /// </summary>
    [HttpGet("{id}/script-settings")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<RunnerScriptEngineSettings>> GetScriptSettings(Guid id, CancellationToken cancellationToken)
    {
        var settings = await runnersService.GetScriptEngineSettings(id, cancellationToken);

        if (settings == null)
        {
            return NotFound();
        }

        return Ok(settings);
    }

    /// <summary>
    /// Update script engine settings for a specific runner
    /// </summary>
    [HttpPut("{id}/script-settings")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> UpdateScriptSettings(Guid id, [FromBody] RunnerScriptEngineSettings settings, CancellationToken cancellationToken)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        await runnersService.UpdateScriptEngineSettings(id, json, cancellationToken);
        return NoContent();
    }

    [HttpGet("by-name/{name}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<RunnerDetails>> GetByName(string name, CancellationToken cancellationToken)
    {
        var runner = await runnersService.GetByNameAsync(name, cancellationToken);

        if (runner == null)
        {
            return NotFound();
        }

        return Ok(runner);
    }
}
