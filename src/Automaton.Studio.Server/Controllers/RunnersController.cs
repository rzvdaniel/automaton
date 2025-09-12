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
}
