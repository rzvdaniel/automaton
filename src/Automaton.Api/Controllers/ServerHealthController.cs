using Automaton.Api.Authentication;
using Automaton.Api.ServerHealth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>
/// Read-only server health for apps with an approved <c>server:read</c> permission (and system administrators).
/// The figures come from the Machine Management Runner; apps get no shell or file access.
/// </summary>
[ApiController]
[Route("api/server/health")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = AppPermissionAuthenticationOptions.ServerReadPolicy)]
public sealed class ServerHealthController(ServerHealthService health) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ServerHealthResponse>> Get(CancellationToken token)
        => Ok(await health.GetAsync(DateTime.UtcNow, token));
}
