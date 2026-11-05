using Automaton.Api.Models;
using Automaton.Api.Authentication;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Scripting.Engines;
using Microsoft.AspNetCore.Authorization;
using Automaton.Api.Config;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Controllers;

public class RunnersController : BaseController
{
    private readonly RunnerService runnersService;
    private readonly string currentImage;
    public RunnersController(RunnerService runnersService, IOptions<HostedRunnerOptions> hostedRunnerOptions)
    {
        this.runnersService = runnersService;
        currentImage = hostedRunnerOptions.Value.DockerImage;
    }

    [HttpGet]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<ActionResult<IEnumerable<RunnerDetails>>> Get([FromQuery] Guid? projectId, CancellationToken cancellationToken)
    {
        var runners = (await runnersService.List(projectId, cancellationToken)).ToList();
        foreach (var runner in runners)
            runner.UpgradeAvailable = !string.IsNullOrEmpty(runner.InstallationId)
                && !RunnerUpgradeService.IsCurrent(runner.ImageDigest, currentImage);
        return Ok(runners);
    }

    [HttpPost]
    [Authorize(Policy = "AllowApiKey")]
    public ActionResult Post()
    {
        return StatusCode(StatusCodes.Status410Gone, new { message = "Install a Machine Management Runner through the one-time installation flow." });
    }

    [HttpPut("{id}")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> Put(Guid id, Automaton.Api.Models.UpdateRunnerRequest runner, CancellationToken cancellationToken)
    {
        try { await runnersService.Update(id, runner, cancellationToken); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception) { return BadRequest(new { message = exception.Message }); }

        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "DeveloperApiAccess")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken,
        [FromQuery] bool hostCleanupConfirmed = false)
    {
        if (!AppChangeKeyScope.IsAllowed(User))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This API key cannot delete Runners. Use a key with the Developer scope." });
        try
        {
            await runnersService.Delete(id, cancellationToken, hostCleanupConfirmed);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }

        return NoContent();
    }

    [HttpPost("{id}/removal")]
    [Authorize(Policy = "DeveloperApiAccess")]
    public async Task<IActionResult> PrepareRemoval(Guid id, [FromServices] RunnerRemovalService removal,
        CancellationToken cancellationToken)
    {
        if (!AppChangeKeyScope.IsAllowed(User))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This API key cannot prepare Runner removal. Use a key with the Developer scope." });
        try { return Ok(await removal.PrepareAsync(id, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }

    /// <summary>Upgrades an online Runner to the server's configured image.</summary>
    [HttpPost("{id}/upgrade")]
    [Authorize(Policy = "DeveloperApiAccess")]
    public async Task<IActionResult> Upgrade(Guid id, [FromServices] RunnerUpgradeService upgrade, CancellationToken cancellationToken)
    {
        if (!AppChangeKeyScope.IsAllowed(User))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This API key cannot upgrade Runners. Use a key with the Developer scope." });
        try { return Accepted(await upgrade.StartAsync(id, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }

    /// <summary>Reports whether a started upgrade is in progress, reconnected or rolled back.</summary>
    [HttpGet("{id}/upgrade")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> UpgradeStatus(Guid id, [FromServices] RunnerUpgradeService upgrade, CancellationToken cancellationToken)
    {
        try { return Ok(await upgrade.StatusAsync(id, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception) { return Conflict(new { message = exception.Message }); }
    }

    /// <summary>The host command for upgrading an offline or broken Runner by hand.</summary>
    [HttpPost("{id}/upgrade-command")]
    [Authorize(Policy = "AllowApiKey")]
    public async Task<IActionResult> PrepareUpgrade(Guid id, [FromServices] RunnerUpgradeService upgrade,
        CancellationToken cancellationToken)
    {
        try { return Ok(await upgrade.PrepareAsync(id, cancellationToken)); }
        catch (UnauthorizedAccessException) { return Forbid(); }
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
        try { await runnersService.UpdateScriptEngineSettings(id, json, cancellationToken); }
        catch (UnauthorizedAccessException) { return Forbid(); }
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
