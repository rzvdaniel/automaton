using Automaton.Api.Authentication;
using Automaton.Api.ServerCleanup;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Maintenance on one chosen machine for an app that holds the approved <c>runner:maintain</c> permission. Changing anything
/// also needs the viewer token of a system administrator and that machine approved for the app; reading a run needs neither.
/// </summary>
[ApiController]
[Route("api/server/runners/{runnerId:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = AppPermissionAuthenticationOptions.RunnerMaintainPolicy)]
public sealed class RunnerMaintenanceController(RunnerMaintenanceService maintenance, ServerCleanupService cleanup,
    ViewerTokenService viewerTokens, ResourceAuthorizationService authorization) : ControllerBase
{
    [HttpPost("cleanup")]
    public Task<IActionResult> RunCleanup(Guid runnerId, CancellationToken token)
        => ChangeAsync(runnerId, async (appId, viewer) => Accepted(await maintenance.RequestCleanupAsync(appId, viewer, runnerId, DateTime.UtcNow, token)), token);

    /// <summary>The apps on this machine.</summary>
    [HttpGet("apps")]
    public async Task<IActionResult> Apps(Guid runnerId, CancellationToken token)
        => !Guid.TryParse(User.FindFirst("app_id")?.Value, out var appId) || !await maintenance.IsApprovedAsync(appId, runnerId, token)
            ? StatusCode(StatusCodes.Status403Forbidden) : Ok(await maintenance.ListAppsAsync(runnerId, token));

    [HttpPost("apps/{targetId:guid}/logs")]
    public Task<IActionResult> Logs(Guid runnerId, Guid targetId, [FromQuery] int lines = 100, CancellationToken token = default)
        => ChangeAsync(runnerId, async (appId, viewer) => Ok(await maintenance.ReadLogsAsync(appId, viewer, runnerId, targetId, lines, token)), token);

    [HttpPost("apps/{targetId:guid}/restart")]
    public Task<IActionResult> Restart(Guid runnerId, Guid targetId, CancellationToken token)
        => ChangeAsync(runnerId, async (appId, viewer) => Ok(await maintenance.RestartAppAsync(appId, viewer, runnerId, targetId, token)), token);

    [HttpPost("disk-report")]
    public Task<IActionResult> DiskReport(Guid runnerId, CancellationToken token)
        => ChangeAsync(runnerId, async (appId, viewer) => Ok(await maintenance.DiskReportAsync(appId, viewer, runnerId, token)), token);

    /// <summary>Every operation needs the machine approved for the app and the viewer token of a system administrator.</summary>
    private async Task<IActionResult> ChangeAsync(Guid runnerId, Func<Guid, Guid, Task<IActionResult>> operation, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirst("app_id")?.Value, out var appId)) return StatusCode(StatusCodes.Status403Forbidden);
        var viewer = viewerTokens.Validate(Request.Headers[ViewerHeaders.Token].FirstOrDefault(), appId);
        if (viewer is null || !await authorization.IsSystemAdministratorAsync(viewer.Value, token)) return StatusCode(StatusCodes.Status403Forbidden);
        if (!await maintenance.IsApprovedAsync(appId, runnerId, token)) return StatusCode(StatusCodes.Status403Forbidden);
        try { return await operation(appId, viewer.Value); }
        catch (KeyNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }

    [HttpGet("cleanup/{runId:guid}")]
    public async Task<IActionResult> GetRun(Guid runnerId, Guid runId, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirst("app_id")?.Value, out var appId)) return StatusCode(StatusCodes.Status403Forbidden);
        if (!await maintenance.IsApprovedAsync(appId, runnerId, token)) return StatusCode(StatusCodes.Status403Forbidden);
        var run = await cleanup.FindRunAsync(runnerId, runId, token);
        return run is null ? NotFound() : Ok(run);
    }

    /// <summary>Which machines this app may maintain, so the app can show or hide the action.</summary>
    [HttpGet("/api/server/runners/permissions")]
    public async Task<IActionResult> Permissions(CancellationToken token)
        => Guid.TryParse(User.FindFirst("app_id")?.Value, out var appId) ? Ok(await maintenance.ListAsync(appId, token)) : StatusCode(StatusCodes.Status403Forbidden);
}
