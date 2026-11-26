using Automaton.Api.Authentication;
using Automaton.Api.ServerHealth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Read-only fail2ban state for apps with an approved <c>security:read</c> permission (and system administrators).
/// The Machine Management Runner reads fail2ban's database on the host; apps get no socket, firewall or shell access.
/// Unban and allowlist (<c>security:manage</c>) are in <see cref="ServerSecurityManageController"/>.
/// </summary>
[ApiController]
[Route("api/server/security")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = AppPermissionAuthenticationOptions.SecurityReadPolicy)]
public sealed class ServerSecurityController(Fail2BanReadService fail2Ban, HostSecurityReadService hostSecurity) : ControllerBase
{
    [HttpGet("fail2ban")]
    public async Task<ActionResult<Fail2BanResponse>> Get(CancellationToken token)
        => Ok(await fail2Ban.GetAsync(DateTime.UtcNow, token));

    /// <summary>SSH, open ports and firewall findings from the Runner's last host security check.</summary>
    [HttpGet("host")]
    public async Task<ActionResult<HostSecurityResponse>> GetHost(CancellationToken token)
        => Ok(await hostSecurity.GetAsync(DateTime.UtcNow, token));
}
