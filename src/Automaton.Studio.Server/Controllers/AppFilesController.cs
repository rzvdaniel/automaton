using Automaton.Core.Models;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Enums;
using Automaton.Studio.Server.Hubs;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Automaton.Studio.Server.Controllers;

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
    private const int ChunkSize = 256 * 1024; // 256 KB
    private const int UploadTimeoutMinutes = 10;
    private const int DownloadChunkTimeoutSeconds = 30;

    private readonly ApplicationDbContext dataContext;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly FileUploadRegistry uploadRegistry;
    private readonly FileDownloadRegistry downloadRegistry;
    private readonly ILogger<AppFilesController> logger;

    public AppFilesController(
        ApplicationDbContext dataContext,
        IHubContext<AutomatonHub> automatonHub,
        FileUploadRegistry uploadRegistry,
        FileDownloadRegistry downloadRegistry,
        ILogger<AppFilesController> logger)
    {
        this.dataContext = dataContext;
        this.automatonHub = automatonHub;
        this.uploadRegistry = uploadRegistry;
        this.downloadRegistry = downloadRegistry;
        this.logger = logger;
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

        return await UploadInternal(appId, ownerId, file, ct);
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

        return await UploadInternal(app.Id, app.OwnerId.Value, file, ct);
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

        await DownloadInternal(appId, ownerId, sessionId, ct);
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

        await DownloadInternal(app.Id, app.OwnerId.Value, sessionId, ct);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private async Task<IActionResult> UploadInternal(Guid appId, Guid ownerId, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        var runnerError = await GetRunnerErrorAsync(ownerId, appId);
        if (runnerError is not null)
            return BadRequest(new { error = runnerError });

        var runner = await ResolveRunnerAsync(ownerId, appId);
        if (runner is null)
            return BadRequest(new { error = "Runner is no longer available. Please try again." });

        var (sessionId, completionTask) = uploadRegistry.Register(
            TimeSpan.FromMinutes(UploadTimeoutMinutes), ct);

        var fileName = SanitizeFileName(file.FileName);
        var contentType = file.ContentType ?? "application/octet-stream";

        logger.LogInformation(
            "[AppFilesController] Upload started: SessionId={SessionId} AppId={AppId} Runner={Runner} File={File} Size={Size}",
            sessionId, appId, runner.Name, fileName, file.Length);

        try
        {
            using var stream = file.OpenReadStream();
            var buffer = new byte[ChunkSize];
            int chunkIndex = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                var isLast = stream.Position >= stream.Length;
                var chunkData = bytesRead == ChunkSize ? buffer : buffer[..bytesRead];

                var chunk = new FileUploadChunk
                {
                    SessionId = sessionId,
                    ChunkIndex = chunkIndex++,
                    Data = chunkData,
                    IsLast = isLast,
                    FileName = fileName,
                    ContentType = contentType
                };

                await automatonHub.Clients.Client(runner.ConnectionId!)
                    .SendAsync(AutomatonHubMethods.ReceiveFileChunk, chunk, ct);
            }

            // Wait for runner to ACK that all chunks are written to disk
            await completionTask;

            logger.LogInformation(
                "[AppFilesController] Upload complete: SessionId={SessionId} Chunks={ChunkCount}",
                sessionId, chunkIndex);

            return Ok(new { session_id = sessionId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[AppFilesController] Upload failed: SessionId={SessionId} AppId={AppId}", sessionId, appId);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private async Task DownloadInternal(Guid appId, Guid ownerId, string sessionId, CancellationToken ct)
    {
        var runner = await ResolveRunnerAsync(ownerId, appId);
        if (runner is null)
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = "No runner available." }, ct);
            return;
        }

        var channel = downloadRegistry.Register(sessionId);

        logger.LogInformation(
            "[AppFilesController] Download started: SessionId={SessionId} AppId={AppId} Runner={Runner}",
            sessionId, appId, runner.Name);

        try
        {
            long offset = 0;
            bool headerWritten = false;

            while (true)
            {
                var request = new FetchFileChunkRequest
                {
                    SessionId = sessionId,
                    Offset = offset,
                    ChunkSize = ChunkSize
                };

                await automatonHub.Clients.Client(runner.ConnectionId!)
                    .SendAsync(AutomatonHubMethods.FetchFileChunk, request, ct);

                using var chunkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                chunkCts.CancelAfter(TimeSpan.FromSeconds(DownloadChunkTimeoutSeconds));

                FileChunkReadyMessage chunk;
                try
                {
                    chunk = await channel.Reader.ReadAsync(chunkCts.Token);
                }
                catch (OperationCanceledException)
                {
                    logger.LogWarning("[AppFilesController] Download chunk timeout: SessionId={SessionId}", sessionId);
                    break;
                }

                if (!headerWritten)
                {
                    Response.ContentType = chunk.ContentType;
                    Response.Headers.ContentDisposition =
                        $"attachment; filename=\"{Uri.EscapeDataString(chunk.FileName)}\"";
                    headerWritten = true;
                }

                await Response.Body.WriteAsync(chunk.Data, ct);
                await Response.Body.FlushAsync(ct);

                if (chunk.IsLast)
                    break;

                offset += chunk.Data.Length;
            }

            logger.LogInformation(
                "[AppFilesController] Download complete: SessionId={SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[AppFilesController] Download failed: SessionId={SessionId} AppId={AppId}", sessionId, appId);
        }
        finally
        {
            downloadRegistry.Complete(sessionId);
        }
    }

    /// <summary>
    /// Returns a user-facing error string explaining why no runner is available, or null if a runner can be resolved.
    /// </summary>
    private async Task<string?> GetRunnerErrorAsync(Guid ownerId, Guid appId)
    {
        var executionRunnerId = await dataContext.WebApps
            .Where(a => a.Id == appId)
            .Select(a => a.ExecutionRunnerId)
            .FirstOrDefaultAsync();

        if (executionRunnerId == null)
            return "No runner is configured for this app. Go to App Settings and select a runner.";

        var runner = await dataContext.Runners
            .Where(r => r.Id == executionRunnerId)
            .FirstOrDefaultAsync();

        if (runner is null)
            return "The runner configured for this app no longer exists.";

        if (!RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
            return $"Runner '{runner.Name}' is not connected. Please start the runner and try again.";

        if (!await dataContext.Runners.AnyAsync(r => r.Id == executionRunnerId && r.RunnerUsers.Any(ru => ru.UserId == ownerId)))
            return $"Runner '{runner.Name}' is not accessible by this app's owner.";

        return null;
    }

    private async Task<Runner?> ResolveRunnerAsync(Guid ownerId, Guid appId)
    {
        var thresholdUtc = DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold;
        return await dataContext.Runners
            .Where(r => r.RunnerUsers.Any(ru => ru.UserId == ownerId))
            .Where(r => r.ConnectionId != null
                && r.LastHeartbeatAtUtc.HasValue
                && r.LastHeartbeatAtUtc.Value >= thresholdUtc)
            .Where(r => r.Id == dataContext.WebApps
                .Where(a => a.Id == appId)
                .Select(a => a.ExecutionRunnerId)
                .FirstOrDefault())
            .FirstOrDefaultAsync();
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)
                 ?? User.Claims.FirstOrDefault(c => c.Type == "uid");
        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }

    private static string SanitizeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "download";
        var name = Path.GetFileName(raw.Replace('\0', '_').Trim());
        return string.IsNullOrWhiteSpace(name) ? "download" : name;
    }
}
