using Automaton.Core.Enums;
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
    public async Task<IActionResult> Put(Guid id, RunnerDetails runner, CancellationToken cancellationToken)
    {
        await runnersService.Update(id, runner, cancellationToken);

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
    /// Get Python configuration for a specific runner
    /// </summary>
    [HttpGet("{id}/python-config")]
    [Authorize]
    [ProducesResponseType(typeof(RunnerPythonConfig), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPythonConfig(Guid id)
    {
        var runner = runnersService.Get(id);
        
        if (runner == null)
            return NotFound();
        
        var config = await runnersService.GetPythonConfigAsync(runner);
        return Ok(config);
    }

    /// <summary>
    /// Update Python configuration for a runner
    /// </summary>
    [HttpPut("{id}/python-config")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdatePythonConfig(Guid id, [FromBody] RunnerPythonConfig config)
    {
        var runner = runnersService.Get(id);
        
        if (runner == null)
            return NotFound();
        
        await runnersService.UpdatePythonConfigAsync(runner, config);
        
        return Ok(new { Message = "Python configuration updated successfully" });
    }
}
