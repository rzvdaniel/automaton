using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/apps/{appId:guid}/permissions")]
[Authorize(Roles = UserRole.SystemAdministrator)]
public sealed class AdminAppPermissionsController(AppPermissionService permissions) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid appId, CancellationToken token)
    {
        try { return Ok(await permissions.GetStatusAsync(appId, token)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPut("{permission}")]
    public async Task<IActionResult> Set(Guid appId, string permission, [FromBody] SetAppPermissionRequest request,
        CancellationToken token)
    {
        try { return Ok(await permissions.SetApprovalAsync(appId, permission, request.Approved, token)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException error) { return BadRequest(new { message = error.Message }); }
        catch (Exception error) when (error is InvalidOperationException or AppPermissionConflictException) { return Conflict(new { message = error.Message }); }
    }
}

public sealed record SetAppPermissionRequest(bool Approved);
