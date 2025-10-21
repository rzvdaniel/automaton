using Automaton.Core.Enums;
using Automaton.Core.Scripting;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

public class RunnersController : BaseController
{
    private readonly RunnerService runnersService;

    public RunnersController(RunnerService runnersService)
    {
        this.runnersService = runnersService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RunnerDetails>>> Get(CancellationToken cancellationToken)
    {
        return Ok(await runnersService.List(cancellationToken));
    }

    [HttpGet("find")]
    public async Task<ActionResult<IEnumerable<RunnerDetails>>> Get(IEnumerable<Guid> runnerIds, CancellationToken cancellationToken)
    {
        return Ok(await runnersService.List(runnerIds, cancellationToken));
    }

    [HttpPost]
    [Authorize(Policy = "AllowApiKey")]
    public ActionResult Post(RunnerDetails runner, CancellationToken cancellationToken)
    {
        return Ok(runnersService.Add(runner));
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> Put(Guid id, RunnerDetails runner, CancellationToken cancellationToken)
    {
        await runnersService.Update(id, runner, cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await runnersService.Delete(id, cancellationToken);

        return NoContent();
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
    public async Task<IActionResult> UpdateScriptSettings(Guid id, [FromBody] object settings, CancellationToken cancellationToken)
    {
        await runnersService.UpdateScriptEngineSettings(id, settings?.ToString(), cancellationToken);
        return NoContent();
    }
}
