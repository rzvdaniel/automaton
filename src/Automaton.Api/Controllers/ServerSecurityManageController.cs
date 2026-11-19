using Automaton.Api.Authentication;
using Automaton.Api.ServerHealth;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Unban or allowlist one address in fail2ban for an app with an approved <c>security:manage</c> permission. An app must also
/// present the viewer token of a system administrator issued for it; a signed-in system administrator or admin API key needs no token.
/// Each change is audit-logged. The app gets no socket, firewall or shell access.
/// </summary>
[ApiController]
[Route("api/server/security/fail2ban")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = AppPermissionAuthenticationOptions.SecurityManagePolicy)]
public sealed class ServerSecurityManageController(Fail2BanManageService manage, ViewerTokenService viewerTokens, ResourceAuthorizationService authorization) : ControllerBase
{
    public sealed record AddressRequest(string? Ip);

    [HttpPost("unban")]
    public Task<IActionResult> Unban([FromBody] AddressRequest? body, CancellationToken token)
        => ChangeAsync((appId, viewer) => manage.UnbanAsync(appId, viewer, body?.Ip, token), token);

    [HttpPost("allowlist")]
    public Task<IActionResult> Allowlist([FromBody] AddressRequest? body, CancellationToken token)
        => ChangeAsync((appId, viewer) => manage.AllowlistAsync(appId, viewer, body?.Ip, token), token);

    private async Task<IActionResult> ChangeAsync(Func<Guid, Guid, Task<Fail2BanActionResult>> operation, CancellationToken token)
    {
        var appId = Guid.Empty;
        var viewer = Guid.Empty;
        if (Guid.TryParse(User.FindFirst("app_id")?.Value, out appId))
        {
            var validated = viewerTokens.Validate(Request.Headers[ViewerHeaders.Token].FirstOrDefault(), appId);
            if (validated is null || !await authorization.IsSystemAdministratorAsync(validated.Value, token)) return StatusCode(StatusCodes.Status403Forbidden);
            viewer = validated.Value;
        }
        else if (!Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out viewer)) viewer = Guid.Empty;
        try { return Ok(await operation(appId, viewer)); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
    }
}
