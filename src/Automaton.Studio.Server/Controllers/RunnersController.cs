using Automaton.Core.Enums;
using Automaton.Core.Scripting.Engines;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers;

public class RunnersController : BaseController
{
    private readonly RunnerService runnersService;
    private readonly HostedRunnerManagementService hostedRunnerManagementService;

    public RunnersController(RunnerService runnersService, HostedRunnerManagementService hostedRunnerManagementService)
    {
        this.runnersService = runnersService;
        this.hostedRunnerManagementService = hostedRunnerManagementService;
    }

    [HttpGet]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<IEnumerable<RunnerDetails>>> Get([FromQuery] Guid? projectId, CancellationToken cancellationToken)
    {
        return Ok(await runnersService.List(projectId, cancellationToken));
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
    [Authorize]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await runnersService.Delete(id, cancellationToken);

        return NoContent();
    }

    [HttpPost("{id}/restart-hosted")]
    [Authorize]
    public async Task<IActionResult> RestartHostedRunner(Guid id, CancellationToken cancellationToken)
    {
        await hostedRunnerManagementService.RestartOwnedHostedRunnerByRunnerIdAsync(id, cancellationToken);
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
