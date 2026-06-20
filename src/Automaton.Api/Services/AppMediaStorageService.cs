using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Hubs;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public class AppMediaStorageService
{
    private const int ChunkSize = 256 * 1024;
    private const int UploadTimeoutMinutes = 10;
    private const int DownloadChunkTimeoutSeconds = 30;

    private readonly ApplicationDbContext dataContext;
    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly FileUploadRegistry uploadRegistry;
    private readonly FileDownloadRegistry downloadRegistry;
    private readonly MediaStorageOptions options;
    private readonly ILogger<AppMediaStorageService> logger;

    public AppMediaStorageService(
        ApplicationDbContext dataContext,
        IHubContext<AutomatonHub> automatonHub,
        FileUploadRegistry uploadRegistry,
        FileDownloadRegistry downloadRegistry,
        IOptions<MediaStorageOptions> options,
        ILogger<AppMediaStorageService> logger)
    {
        this.dataContext = dataContext;
        this.automatonHub = automatonHub;
        this.uploadRegistry = uploadRegistry;
        this.downloadRegistry = downloadRegistry;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<(bool Success, int StatusCode, object Payload)> UploadAsync(Guid appId, Guid ownerId, IFormFile? file, CancellationToken ct)
    {
        if (!AppMediaStoragePolicy.TryValidateUpload(file, options.MaxUploadBytes, out var error, out _, out var contentType))
        {
            return (false, StatusCodes.Status400BadRequest, new { error });
        }

        var runnerError = await GetRunnerErrorAsync(ownerId, appId, ct);
        if (runnerError is not null)
        {
            return (false, StatusCodes.Status400BadRequest, new { error = runnerError });
        }

        var runner = await ResolveRunnerAsync(ownerId, appId, ct);
        if (runner is null)
        {
            return (false, StatusCodes.Status400BadRequest, new { error = "Runner is no longer available. Please try again." });
        }

        var stats = await automatonHub.Clients.Client(runner.ConnectionId!)
            .InvokeAsync<AppMediaStatsResponse>(AutomatonHubMethods.GetAppMediaStats, appId.ToString(), ct);

        if (!stats.Success)
        {
            return (false, StatusCodes.Status500InternalServerError, new { error = stats.Error ?? "Unable to inspect app media usage." });
        }

        if (stats.TotalBytes + file!.Length > options.DefaultQuotaBytes)
        {
            return (false, StatusCodes.Status400BadRequest, new { error = $"Media quota exceeded. Limit is {options.DefaultQuotaBytes} bytes." });
        }

        var relativePath = AppMediaStoragePolicy.BuildStorageRelativePath(file.FileName, DateTime.UtcNow);
        var publicPath = AppMediaPathHelper.ToPublicPath(relativePath);
        var (sessionId, completionTask) = uploadRegistry.Register(TimeSpan.FromMinutes(UploadTimeoutMinutes), ct);

        try
        {
            using var stream = file.OpenReadStream();
            var buffer = new byte[ChunkSize];
            var chunkIndex = 0;
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
                    FileName = Path.GetFileName(relativePath),
                    ContentType = contentType,
                    AppId = appId,
                    RelativePath = relativePath,
                    StorageScope = FileTransferStorageScopes.Media
                };

                await automatonHub.Clients.Client(runner.ConnectionId!)
                    .SendAsync(AutomatonHubMethods.ReceiveFileChunk, chunk, ct);
            }

            await completionTask;

            return (true, StatusCodes.Status200OK, new
            {
                success = true,
                path = publicPath,
                fileName = Path.GetFileName(relativePath),
                contentType,
                size = file.Length
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload media for app {AppId}", appId);
            return (false, StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
    }

    public async Task<int> ProxyReadAsync(Guid appId, Guid ownerId, string mediaPath, HttpResponse response, CancellationToken ct)
    {
        string normalizedPath;
        try
        {
            normalizedPath = AppMediaPathHelper.NormalizeRelativePath(mediaPath);
        }
        catch (ArgumentException)
        {
            return StatusCodes.Status400BadRequest;
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCodes.Status403Forbidden;
        }

        if (!AppMediaStoragePolicy.TryGetContentTypeForPath(normalizedPath, out _))
        {
            return StatusCodes.Status400BadRequest;
        }

        var runner = await ResolveRunnerAsync(ownerId, appId, ct);
        if (runner is null)
        {
            return StatusCodes.Status404NotFound;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var channel = downloadRegistry.Register(sessionId);

        try
        {
            long offset = 0;
            var wroteHeaders = false;

            while (true)
            {
                var request = new FetchFileChunkRequest
                {
                    SessionId = sessionId,
                    Offset = offset,
                    ChunkSize = ChunkSize,
                    AppId = appId,
                    RelativePath = normalizedPath,
                    StorageScope = FileTransferStorageScopes.Media
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
                    return StatusCodes.Status504GatewayTimeout;
                }

                if (chunk.StatusCode != StatusCodes.Status200OK)
                {
                    return chunk.StatusCode;
                }

                if (!wroteHeaders)
                {
                    response.ContentType = chunk.ContentType;
                    response.Headers["X-Content-Type-Options"] = "nosniff";
                    response.Headers["Cache-Control"] = "public, max-age=3600";
                    response.Headers["Content-Disposition"] = BuildInlineContentDispositionHeader(normalizedPath);
                    wroteHeaders = true;
                }

                if (chunk.Data.Length > 0)
                {
                    await response.Body.WriteAsync(chunk.Data, ct);
                    await response.Body.FlushAsync(ct);
                    offset += chunk.Data.Length;
                }

                if (chunk.IsLast)
                {
                    return StatusCodes.Status200OK;
                }
            }
        }
        finally
        {
            downloadRegistry.Complete(sessionId);
        }
    }

    private async Task<string?> GetRunnerErrorAsync(Guid ownerId, Guid appId, CancellationToken ct)
    {
        var executionRunnerId = await dataContext.WebApps
            .Where(a => a.Id == appId)
            .Select(a => a.ExecutionRunnerId)
            .FirstOrDefaultAsync(ct);

        if (executionRunnerId == null)
            return "No runner is configured for this app. Go to App Settings and select a runner.";

        var runner = await dataContext.Runners
            .Where(r => r.Id == executionRunnerId)
            .FirstOrDefaultAsync(ct);

        if (runner is null)
            return "The runner configured for this app no longer exists.";

        if (!RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
            return $"Runner '{runner.Name}' is not connected. Please start the runner and try again.";

        if (!await dataContext.Runners.AnyAsync(r => r.Id == executionRunnerId && r.RunnerUsers.Any(ru => ru.UserId == ownerId), ct))
            return $"Runner '{runner.Name}' is not accessible by this app's owner.";

        return null;
    }

    private async Task<Runner?> ResolveRunnerAsync(Guid ownerId, Guid appId, CancellationToken ct)
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
            .FirstOrDefaultAsync(ct);
    }

    private static string BuildInlineContentDispositionHeader(string normalizedPath)
    {
        var fileName = Path.GetFileName(normalizedPath).Replace("\"", string.Empty, StringComparison.Ordinal);
        return $"inline; filename=\"{fileName}\"";
    }
}
