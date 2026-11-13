using Automaton.Api.Authentication;
using Automaton.Api.ServerCleanup;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// The nightly host cleanup for system administrators (and the Server Console app once an administrator approved its
/// <c>server:cleanup</c> permission): read the settings and runs, change the schedule, run it now.
/// </summary>
[ApiController]
[Route("api/server/cleanup")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = AppPermissionAuthenticationOptions.ServerCleanupPolicy)]
public sealed class ServerCleanupController(ServerCleanupService cleanup, ViewerTokenService viewerTokens, ResourceAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ServerCleanupResponse>> Get(CancellationToken token)
        => Ok(await cleanup.GetAsync(DateTime.UtcNow, token));

    [HttpPut("settings")]
    public async Task<ActionResult<ServerCleanupResponse>> Update([FromBody] ServerCleanupSettingsDto request, CancellationToken token)
    {
        if (!await MayChangeAsync(token)) return StatusCode(StatusCodes.Status403Forbidden);
        try { return Ok(await cleanup.UpdateAsync(request, DateTime.UtcNow, token)); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPost("run")]
    public async Task<ActionResult<ServerCleanupResponse>> Run(CancellationToken token)
    {
        if (!await MayChangeAsync(token)) return StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            await cleanup.RunNowAsync(DateTime.UtcNow, token);
            return Accepted(await cleanup.GetAsync(DateTime.UtcNow, token));
        }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }

    /// <summary>
    /// Changes need a system administrator. A signed-in administrator or Developer API key already passed the policy; an app
    /// (approved credential) must also present the viewer token of a system administrator issued for that app, so a
    /// shared app can never change the server for someone who is not one. Reads need only the app credential.
    /// </summary>
    private async Task<bool> MayChangeAsync(CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirst("app_id")?.Value, out var appId)) return true;
        var viewer = viewerTokens.Validate(Request.Headers[ViewerHeaders.Token].FirstOrDefault(), appId);
        return viewer is not null && await authorization.IsSystemAdministratorAsync(viewer.Value, token);
    }
}
