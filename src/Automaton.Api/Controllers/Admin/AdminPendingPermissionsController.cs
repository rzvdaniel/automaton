using Automaton.Api.Authentication;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Admin;

/// <summary>Apps with app permissions waiting for a decision; read by <c>automaton permissions list --pending</c>.</summary>
[ApiController]
[Route("api/admin/app-permissions/pending")]
[Authorize(Policy = PermissionsAdminKey.PolicyName)]
public sealed class AdminPendingPermissionsController(AdminAppDirectoryService directory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token) => Ok(await directory.ListAsync(null, true, 1, 100, token));
}
