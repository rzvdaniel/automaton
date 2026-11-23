using Automaton.Api.Enums;
using Automaton.Api.ServerCleanup;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

/// <summary>The server-wide switch for <c>runner:host-exec</c> (apps running commands as root on a machine). Off by default; a system administrator turns it on.</summary>
[ApiController]
[Route("api/admin/host-exec")]
[Authorize(Roles = UserRole.SystemAdministrator)]
public sealed class AdminHostExecController(HostExecService hostExec, UserContextService userContext) : ControllerBase
{
    public sealed record HostExecSwitchRequest(bool Enabled);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token) => Ok(new HostExecSettingsDto(await hostExec.IsEnabledAsync(token)));

    [HttpPut]
    public async Task<IActionResult> Set([FromBody] HostExecSwitchRequest request, CancellationToken token)
        => Ok(await hostExec.SetEnabledAsync(request.Enabled, userContext.GetUserId(), token));
}
