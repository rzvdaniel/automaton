using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

public sealed record AppPermissionsResponse(
    IReadOnlyList<AppPermissionStatus> Permissions,
    IReadOnlyList<string> Waiting,
    string? ApprovalUrl);

/// <summary>
/// The permissions an app requests and their approval state, for the app's owners and their agents (API keys
/// allowed). Read-only: only a system administrator approves, in Studio, at <c>ApprovalUrl</c>.
/// </summary>
[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/apps/{appId:guid}/permissions")]
public sealed class AppPermissionsController(
    AppPermissionService permissions,
    ResourceAuthorizationService authorization,
    UserContextService users,
    ConfigurationService configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid appId, CancellationToken token)
    {
        if (!await authorization.CanUpdateAppAsync(appId, users.GetUserId(), token)) return Forbid();
        try
        {
            var statuses = await permissions.GetStatusAsync(appId, token);
            var waiting = AppPermissionService.Waiting(statuses);
            return Ok(new AppPermissionsResponse(statuses, waiting,
                waiting.Count > 0 ? AppPermissionService.ApprovalUrl(configuration.WebsiteUrl, appId) : null));
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
