using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>The "Install Server Console" notice for an existing server. Only a system administrator, and only on their click.</summary>
[ApiController]
[Route("api/server/console")]
[Authorize(Policy = "DeveloperApiAccess", Roles = UserRole.SystemAdministrator)]
public sealed class ServerConsoleController(ServerConsoleProvisioningService provisioning, UserContextService userContext) : ControllerBase
{
    public sealed record ServerConsoleStatus(Guid? AppId);

    [HttpGet]
    public async Task<ActionResult<ServerConsoleStatus>> Get(CancellationToken token)
        => Ok(new ServerConsoleStatus(await provisioning.FindAsync(userContext.GetUserId(), token)));

    [HttpPost("install")]
    public async Task<ActionResult<ServerConsoleStatus>> Install(CancellationToken token)
    {
        try { return Ok(new ServerConsoleStatus(await provisioning.ProvisionAsync(userContext.GetUserId(), null, token))); }
        catch (InvalidOperationException exception) { return Conflict(new { error = exception.Message }); }
        catch (ArgumentException exception) { return BadRequest(new { error = exception.Message }); }
    }
}
