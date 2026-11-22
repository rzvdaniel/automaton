using Automaton.Api.Authentication;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>The last 20 maintenance runs (backup, OS updates, restart, Runner upgrade) for apps with an approved <c>server:read</c> permission.</summary>
[ApiController]
[Route("api/server/maintenance-runs")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = AppPermissionAuthenticationOptions.ServerReadPolicy)]
public sealed class ServerMaintenanceRunsController(ServerMaintenanceHistory history) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServerMaintenanceRunDto>>> Get(CancellationToken token)
        => Ok(await history.ListAsync(DateTimeOffset.UtcNow, token));
}
