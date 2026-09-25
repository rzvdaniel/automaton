using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Automaton.Api.Controllers;

/// <summary>
/// Provides file upload and download between the browser and the runner via chunked SignalR streaming.
///
/// Upload flow:
///   1. Browser POSTs multipart file to /api/web-apps/preview/{appId}/files/upload
///      (or /api/web-apps/{slug}/files/upload for published apps)
///   2. Server reads stream in 256 KB chunks, forwards each as ReceiveFileChunk via SignalR to the runner
///   3. Runner writes chunks to {temp}/uploads/{sessionId}/{filename} on its local volume
///   4. Runner sends FileUploadComplete (success) or FileUploadFailed (error) back via hub; server resolves TCS
///   5. Browser passes session_id in callController inputs — file lives ONLY on runner volume
///
/// Download flow:
///   1. Browser GETs /api/web-apps/preview/{appId}/files/{sessionId}
///   2. Server sends FetchFileChunk to runner (offset=0, chunkSize=256KB)
///   3. Runner reads from {temp}/results/{sessionId}/, sends FileChunkReady with bytes back
///   4. Server pipes each chunk directly to Response.Body — never accumulates the file
///   5. Repeat until IsLast=true
///
/// The server is a pure proxy. No bytes are written to server disk. No caches.
/// </summary>
[ApiController]
[Route("api")]
public class AppFilesController : ControllerBase
{
    private readonly ApplicationDbContext dataContext;
    private readonly AppFileUploadService uploadService;
    private readonly AppFileDownloadService downloadService;
    private readonly PrivateAppLaunchService privateAppLaunchService;

    public AppFilesController(
        ApplicationDbContext dataContext,
        AppFileUploadService uploadService,
        AppFileDownloadService downloadService,
        PrivateAppLaunchService privateAppLaunchService)
    {
        this.dataContext = dataContext;
        this.uploadService = uploadService;
        this.downloadService = downloadService;
        this.privateAppLaunchService = privateAppLaunchService;
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Preview mode upload — requires JWT authentication.
    /// </summary>
    [HttpPost("web-apps/preview/{appId}/files/upload")]
    [Authorize]
    [RequestSizeLimit(200 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 200 * 1024 * 1024)]
    public async Task<IActionResult> UploadPreview(Guid appId, IFormFile file, CancellationToken ct)
    {
        var ownerId = GetUserId();
        if (ownerId == Guid.Empty)
            return Unauthorized();

        return await uploadService.UploadAsync(appId, ownerId, file, ct);
    }

    [HttpPost("web-apps/app/{appId}/files/upload")]
    [Authorize]
    [RequestSizeLimit(200 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 200 * 1024 * 1024)]
    public async Task<IActionResult> UploadPrivate(Guid appId, IFormFile file, CancellationToken ct)
    {
        if (User.HasClaim(c => c.Type == PrivateAppAuthConstants.ScopeClaim)
            && !privateAppLaunchService.IsScopedPrivateAppPrincipal(User, appId))
        {
            return Unauthorized();
        }

        var userId = GetUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var app = await FindOwnedAppAsync(appId, userId, ct);

        if (app is null || app.OwnerId is null)
            return NotFound(new { error = "App not found." });

        return await uploadService.UploadAsync(app.Id, app.OwnerId.Value, file, ct);
    }

    /// <summary>
    /// Published app upload — same auth as published controller execution.
    /// </summary>
    [HttpPost("web-apps/{slug}/files/upload")]
    [AllowAnonymous]
    [RequestSizeLimit(200 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 200 * 1024 * 1024)]
    public async Task<IActionResult> UploadPublished(string slug, IFormFile file, CancellationToken ct)
    {
        var app = await dataContext.WebApps
            .Include(a => a.WebAppUsers)
            .Where(a => a.PublicUrl == slug && a.IsPublished && a.AccessStatus == AppAccessStatuses.Active)
            .Select(a => new { a.Id, OwnerId = a.WebAppUsers.FirstOrDefault() == null ? (Guid?)null : a.WebAppUsers.FirstOrDefault()!.UserId })
            .FirstOrDefaultAsync(ct);

        if (app is null)
            return NotFound(new { error = "App not found." });

        if (app.OwnerId is null)
            return BadRequest(new { error = "App has no owner configured." });

        return await uploadService.UploadAsync(app.Id, app.OwnerId.Value, file, ct);
    }

    // ── Download ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Preview mode download — requires JWT authentication.
    /// </summary>
    [HttpGet("web-apps/preview/{appId}/files/{sessionId}")]
    [Authorize]
    public async Task DownloadPreview(Guid appId, string sessionId, CancellationToken ct)
    {
        var ownerId = GetUserId();
        if (ownerId == Guid.Empty)
        {
            Response.StatusCode = 401;
            return;
        }

        await downloadService.DownloadAsync(appId, ownerId, sessionId, Response, ct);
    }

    [HttpGet("web-apps/app/{appId}/files/{sessionId}")]
    [Authorize]
    public async Task DownloadPrivate(Guid appId, string sessionId, CancellationToken ct)
    {
        if (User.HasClaim(c => c.Type == PrivateAppAuthConstants.ScopeClaim)
            && !privateAppLaunchService.IsScopedPrivateAppPrincipal(User, appId))
        {
            Response.StatusCode = 401;
            return;
        }

        var userId = GetUserId();
        if (userId == Guid.Empty)
        {
            Response.StatusCode = 401;
            return;
        }

        var app = await FindOwnedAppAsync(appId, userId, ct);

        if (app is null || app.OwnerId is null)
        {
            Response.StatusCode = 404;
            await Response.WriteAsJsonAsync(new { error = "App not found." }, ct);
            return;
        }

        await downloadService.DownloadAsync(app.Id, app.OwnerId.Value, sessionId, Response, ct);
    }

    /// <summary>
    /// Published app download — same auth as published controller execution.
    /// </summary>
    [HttpGet("web-apps/{slug}/files/{sessionId}")]
    [AllowAnonymous]
    public async Task DownloadPublished(string slug, string sessionId, CancellationToken ct)
    {
        var app = await dataContext.WebApps
            .Include(a => a.WebAppUsers)
            .Where(a => a.PublicUrl == slug && a.IsPublished && a.AccessStatus == AppAccessStatuses.Active)
            .Select(a => new { a.Id, OwnerId = a.WebAppUsers.FirstOrDefault() == null ? (Guid?)null : a.WebAppUsers.FirstOrDefault()!.UserId })
            .FirstOrDefaultAsync(ct);

        if (app is null)
        {
            Response.StatusCode = 404;
            await Response.WriteAsJsonAsync(new { error = "App not found." }, ct);
            return;
        }

        if (app.OwnerId is null)
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = "App has no owner configured." }, ct);
            return;
        }

        await downloadService.DownloadAsync(app.Id, app.OwnerId.Value, sessionId, Response, ct);
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
                 ?? User.Claims.FirstOrDefault(c => c.Type == "uid");
        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }

    private Task<OwnedApp?> FindOwnedAppAsync(Guid appId, Guid userId, CancellationToken cancellationToken)
    {
        return dataContext.WebApps
            .Include(app => app.WebAppUsers)
            .Where(app => app.Id == appId && app.WebAppUsers.Any(appUser => appUser.UserId == userId))
            .Select(app => new OwnedApp(
                app.Id,
                app.WebAppUsers.FirstOrDefault() == null
                    ? null
                    : app.WebAppUsers.FirstOrDefault()!.UserId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private sealed record OwnedApp(Guid Id, Guid? OwnerId);

}
