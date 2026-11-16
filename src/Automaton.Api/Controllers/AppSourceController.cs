using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers;

/// <summary>
/// Read-only view of an app's stored files (including automaton.json) for the people who manage the app and for
/// administrators. View-only members and strangers are refused.
/// </summary>
[ApiController]
[Authorize]
[Route("api/apps/{appId:guid}/source")]
public sealed class AppSourceController(
    ApplicationDbContext db,
    ResourceAuthorizationService authorization,
    UserContextService users,
    AppFileStorageService storage) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Files(Guid appId, CancellationToken token)
    {
        var owner = await FindOwnerAsync(appId, token);
        if (owner is null) return Forbid();
        return Ok(new { success = true, editable = false, files = await storage.GetAppRootEntriesAsync(owner.Value, appId) });
    }

    [HttpGet("{*path}")]
    public async Task<IActionResult> File(Guid appId, string path, CancellationToken token)
    {
        var owner = await FindOwnerAsync(appId, token);
        if (owner is null) return Forbid();
        if (path.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path))
            return BadRequest(new { success = false, message = "Invalid path" });
        var content = await storage.ReadAppRootFileAsync(owner.Value, appId, path);
        return content is null
            ? NotFound(new { success = false, message = "File not found" })
            : Ok(new { success = true, editable = false, content });
    }

    private async Task<Guid?> FindOwnerAsync(Guid appId, CancellationToken token)
    {
        var userId = users.GetUserId();
        if (!await authorization.CanUpdateAppAsync(appId, userId, token) && !await authorization.IsAdministratorAsync(userId, token)) return null;
        // Same owner the admin file browser reads.
        return await db.WebAppUsers.Where(link => link.WebAppId == appId).OrderBy(link => link.UserId)
            .Select(link => (Guid?)link.UserId).FirstOrDefaultAsync(token);
    }
}
