using Automaton.Api.Enums;
using Automaton.Api.ServerCleanup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

/// <summary>A system administrator approves <c>runner:maintain</c> for an app one machine at a time.</summary>
[ApiController]
[Route("api/admin/apps/{appId:guid}/runner-permissions")]
[Authorize(Roles = UserRole.SystemAdministrator)]
public sealed class AdminAppRunnerPermissionsController(RunnerMaintenanceService maintenance) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid appId, CancellationToken token) => Ok(await maintenance.ListAsync(appId, token));

    [HttpPut("{runnerId:guid}")]
    public async Task<IActionResult> Set(Guid appId, Guid runnerId, [FromBody] SetAppPermissionRequest request, CancellationToken token)
    {
        try { return Ok(await maintenance.SetAsync(appId, runnerId, request.Approved, token)); }
        catch (KeyNotFoundException error) { return NotFound(new { message = error.Message }); }
    }
}
