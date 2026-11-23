using Automaton.Api.Authentication;
using Automaton.Api.Entities;
using Automaton.Api.ServerCleanup;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

/// <summary>
/// A system administrator approves <c>runner:maintain</c> (or, with <c>?permission=runner:host-exec</c>, any command as root) for an app one machine at a time.
/// </summary>
[ApiController]
[Route("api/admin/apps/{appId:guid}/runner-permissions")]
[Authorize(Policy = PermissionsAdminKey.PolicyName)]
public sealed class AdminAppRunnerPermissionsController(RunnerMaintenanceService maintenance, HostExecService hostExec, UserContextService userContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid appId, [FromQuery] string? permission, CancellationToken token)
    {
        if (permission == AppPermissionApproval.RunnerHostExec)
        {
            try { return Ok(await hostExec.ListAsync(appId, token)); }
            catch (KeyNotFoundException error) { return NotFound(new { message = error.Message }); }
        }
        return Ok(await maintenance.ListAsync(appId, token));
    }

    [HttpPut("{runnerId:guid}")]
    public async Task<IActionResult> Set(Guid appId, Guid runnerId, [FromBody] SetAppPermissionRequest request, [FromQuery] string? permission, CancellationToken token)
    {
        try
        {
            return permission == AppPermissionApproval.RunnerHostExec
                ? Ok(await hostExec.SetAsync(appId, runnerId, request.Approved, userContext.GetUserId(), token))
                : Ok(await maintenance.SetAsync(appId, runnerId, request.Approved, token));
        }
        catch (KeyNotFoundException error) { return NotFound(new { message = error.Message }); }
        catch (InvalidOperationException error) { return Conflict(new { message = error.Message }); }
    }
}
