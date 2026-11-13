using Automaton.Api.Authentication;
using Automaton.Api.ServerCleanup;
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
public sealed class ServerCleanupController(ServerCleanupService cleanup) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ServerCleanupResponse>> Get(CancellationToken token)
        => Ok(await cleanup.GetAsync(DateTime.UtcNow, token));

    [HttpPut("settings")]
    public async Task<ActionResult<ServerCleanupResponse>> Update([FromBody] ServerCleanupSettingsDto request, CancellationToken token)
    {
        try { return Ok(await cleanup.UpdateAsync(request, DateTime.UtcNow, token)); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }

    [HttpPost("run")]
    public async Task<ActionResult<ServerCleanupResponse>> Run(CancellationToken token)
    {
        try
        {
            await cleanup.RunNowAsync(DateTime.UtcNow, token);
            return Accepted(await cleanup.GetAsync(DateTime.UtcNow, token));
        }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }
}
