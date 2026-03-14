using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// Provides a short-lived file transfer relay between the runner's Python controllers and the browser.
///
/// Flow:
///   1. Runner Python controller processes a file (audio, PDF, image, etc.)
///   2. Controller POSTs the file to POST /api/runner/files (authenticated with runner API key)
///   3. Server stores it in memory with a 15-minute TTL and returns a download URL + token
///   4. Python controller returns {"download_url": "..."} to the browser via the SignalR tunnel
///   5. Browser hits GET /api/apps/files/{token} directly — no SignalR involved
///
/// This avoids routing large binary payloads through SignalR (the 50 MB limit does not apply).
/// </summary>
[ApiController]
[Route("api")]
public class AppFilesController : ControllerBase
{
    // 50 MB — matches the SignalR MaximumReceiveMessageSize; avoids size surprises for users.
    private const long MaxFileSizeBytes = 50L * 1024 * 1024;

    // Files expire after 15 minutes. The browser should download immediately after controller execution.
    private static readonly TimeSpan FileTtl = TimeSpan.FromMinutes(15);

    private readonly IMemoryCache _cache;
    private readonly ILogger<AppFilesController> _logger;

    public AppFilesController(IMemoryCache cache, ILogger<AppFilesController> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Called by the runner's Python controller to store a processed result file.
    /// Returns a short-lived download URL the browser can access directly.
    /// Requires a runner API key (X-API-Key header or ?api_key= query param).
    /// </summary>
    [HttpPost("runner/files")]
    [Authorize(Policy = "AllowApiKey")]
    [RequestSizeLimit(50 * 1024 * 1024 + 4096)]
    [RequestFormLimits(MultipartBodyLengthLimit = 50 * 1024 * 1024)]
    public async Task<IActionResult> UploadResultFile(
        IFormFile file,
        [FromForm] string? downloadFileName = null)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { error = $"File size ({file.Length:N0} bytes) exceeds the {MaxFileSizeBytes / (1024 * 1024)} MB limit." });

        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms);
        var data = ms.ToArray();

        var token = Guid.NewGuid().ToString("N"); // 32 hex chars, no hyphens
        var fileName = SanitizeFileName(downloadFileName ?? file.FileName);
        var contentType = file.ContentType ?? "application/octet-stream";

        _cache.Set(
            $"app_file:{token}",
            new TempFileEntry(fileName, contentType, data),
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = FileTtl
            });

        _logger.LogInformation(
            "[AppFilesController] Runner stored temp file token={Token} name={FileName} size={Size} type={ContentType}",
            token, fileName, data.Length, contentType);

        var downloadUrl = $"{Request.Scheme}://{Request.Host}/api/apps/files/{token}";
        return Ok(new { token, downloadUrl, expiresInMinutes = (int)FileTtl.TotalMinutes });
    }

    /// <summary>
    /// Browser downloads a previously uploaded result file by its one-time token.
    /// The token is removed from cache after the first successful download.
    /// No authentication required — the unguessable token (128-bit UUID) is the capability credential.
    /// </summary>
    [HttpGet("apps/files/{token}")]
    [AllowAnonymous]
    public IActionResult DownloadResultFile(string token)
    {
        // Validate: must be exactly 32 lowercase hex chars (URI-safe, no path traversal, no cache key injection)
        if (string.IsNullOrEmpty(token) || token.Length != 32 || !IsHex(token))
        {
            _logger.LogWarning("[AppFilesController] Invalid token format: {Token}", token);
            return BadRequest(new { error = "Invalid token." });
        }

        var cacheKey = $"app_file:{token}";
        if (!_cache.TryGetValue(cacheKey, out TempFileEntry? entry) || entry is null)
        {
            _logger.LogInformation("[AppFilesController] Token not found or expired: {Token}", token);
            return NotFound(new { error = "File not found or the download link has expired." });
        }

        // One-time download — remove immediately so the token cannot be replayed
        _cache.Remove(cacheKey);

        _logger.LogInformation(
            "[AppFilesController] Token redeemed: {Token} name={FileName} size={Size}",
            token, entry.FileName, entry.Data.Length);

        return File(entry.Data, entry.ContentType, Uri.EscapeDataString(entry.FileName));
    }

    private static string SanitizeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "download";

        // Strip directory components and null bytes
        var name = Path.GetFileName(raw.Replace('\0', '_').Trim());
        return string.IsNullOrWhiteSpace(name) ? "download" : name;
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }

    private sealed record TempFileEntry(string FileName, string ContentType, byte[] Data);
}
