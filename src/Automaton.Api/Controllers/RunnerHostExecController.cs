using Automaton.Api.Authentication;
using Automaton.Api.ServerCleanup;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// An app that holds the approved <c>runner:host-exec</c> permission runs a script as root on one chosen machine. It also needs the viewer
/// token of a system administrator (who started it); the server switch, the app's owner, the per-machine approval and the approved code
/// are checked by <see cref="HostExecService"/> on every call.
/// </summary>
[ApiController]
[Route("api/server/runners/{runnerId:guid}/host-exec")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = AppPermissionAuthenticationOptions.RunnerHostExecPolicy)]
public sealed class RunnerHostExecController(HostExecService hostExec, ViewerTokenService viewerTokens, ResourceAuthorizationService authorization) : ControllerBase
{
    /// <summary>The script, the input it reads on stdin (for secrets) and an optional timeout in seconds (default 30 minutes, at most 2 hours).</summary>
    public sealed record HostExecBody(string? Script, string? Stdin, int? TimeoutSeconds);

    [HttpPost]
    [RequestSizeLimit(256 * 1024)]
    public async Task<IActionResult> Run(Guid runnerId, [FromBody] HostExecBody? body, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirst("app_id")?.Value, out var appId)) return StatusCode(StatusCodes.Status403Forbidden);
        var viewer = viewerTokens.Validate(Request.Headers[ViewerHeaders.Token].FirstOrDefault(), appId);
        if (viewer is null || !await authorization.IsSystemAdministratorAsync(viewer.Value, token)) return StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            var request = new HostExecRequest(body?.Script ?? "", body?.Stdin, body?.TimeoutSeconds ?? HostExecRequest.DefaultTimeoutSeconds);
            return Ok(await hostExec.RunAsync(appId, viewer.Value, runnerId, request, token));
        }
        catch (HostExecRefusedException exception) { return StatusCode(StatusCodes.Status403Forbidden, new { error = exception.Message }); }
        catch (KeyNotFoundException exception) { return NotFound(new { error = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }
}
