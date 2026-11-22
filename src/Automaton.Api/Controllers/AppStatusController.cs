using Automaton.Api.Authentication;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

/// <summary>Read-only status of all apps for apps with an approved <c>apps:read-status</c> permission (and system administrators).</summary>
[ApiController]
[Route("api/apps/status")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Authorize(Policy = AppPermissionAuthenticationOptions.AppsReadStatusPolicy)]
public sealed class AppStatusController(AppStatusReadService status) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AppStatusItem>>> Get(CancellationToken token)
        => Ok(await status.ListAsync(token));
}
