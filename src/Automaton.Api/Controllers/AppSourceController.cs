using Automaton.Api.Data;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Automaton.Api.Controllers;

/// <summary>
/// Read-only view of an app's source files (the <c>source/</c> folder, including automaton.json) for the people who
/// manage the app and for administrators. View-only members and strangers are refused.
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
    private const int MaxPreviewBytes = 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Files(Guid appId, CancellationToken token)
    {
        var owner = await FindOwnerAsync(appId, token);
        if (owner is null) return Forbid();
        return Ok(new { success = true, editable = false, files = await storage.GetSourceFilesAsync(owner.Value, appId) });
    }

    [HttpGet("{*path}")]
    public async Task<IActionResult> File(Guid appId, string path, CancellationToken token)
    {
        var owner = await FindOwnerAsync(appId, token);
        if (owner is null) return Forbid();
        if (path.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(path))
            return BadRequest(new { success = false, message = "Invalid path" });
        byte[]? bytes;
        try { bytes = await storage.ReadSourceFileBytesAsync(owner.Value, appId, path); }
        catch (UnauthorizedAccessException) { return BadRequest(new { success = false, message = "Invalid path" }); }
        if (bytes is null) return NotFound(new { success = false, message = "File not found" });
        // Text is detected by content: a NUL byte in the first 8 KB means binary; very large files are not previewed.
        if (bytes.Length > MaxPreviewBytes || Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) >= 0)
            return Ok(new { success = true, editable = false, preview = false, content = "" });
        return Ok(new { success = true, editable = false, preview = true, content = Encoding.UTF8.GetString(bytes) });
    }

    private async Task<Guid?> FindOwnerAsync(Guid appId, CancellationToken token)
    {
        var userId = users.GetUserId();
        if (!await authorization.CanUpdateAppAsync(appId, userId, token) && !await authorization.IsAdministratorAsync(userId, token)) return null;
        if (!await db.WebApps.AnyAsync(app => app.Id == appId && app.DeletionClaimId == null, token)) return null;
        // Same owner the admin file browser reads.
        return await db.WebAppUsers.Where(link => link.WebAppId == appId).OrderBy(link => link.UserId)
            .Select(link => (Guid?)link.UserId).FirstOrDefaultAsync(token);
    }
}
