using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>Pause an app without deleting it, and resume it later. Studio, the CLI and API keys all use these endpoints.</summary>
[ApiController]
[Route("api/apps/{id:guid}")]
public sealed class AppPauseController(
    AppPauseService pauseService,
    AppRestartService restartService,
    UserContextService userContextService,
    ConfirmedEmailGuardService confirmedEmailGuardService) : ControllerBase
{
    [HttpPost("pause")]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> Pause(Guid id, CancellationToken cancellationToken) =>
        RunAsync(() => pauseService.PauseAsync(id, User.IsInRole("Administrators"), cancellationToken));

    [HttpPost("resume")]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> Resume(Guid id, CancellationToken cancellationToken) =>
        RunAsync(() => pauseService.ResumeAsync(id, User.IsInRole("Administrators"), cancellationToken));

    /// <summary>Restarts the app so it loads its current settings and secrets. 409 while the app is paused.</summary>
    [HttpPost("restart")]
    [Authorize(Policy = "AllowApiKey")]
    public Task<IActionResult> Restart(Guid id, CancellationToken cancellationToken) =>
        RunAsync(async () => new { appId = id, restarted = await restartService.RestartAsync(id, User.IsInRole("Administrators"), cancellationToken) });

    private async Task<IActionResult> RunAsync<T>(Func<Task<T>> action)
    {
        if (!await confirmedEmailGuardService.IsConfirmedAsync(userContextService.GetUserId()))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        try
        {
            return Ok(await action());
        }
        catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (RuntimeCommandFailedException ex) { return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message }); }
    }
}
