using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Hubs;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace Automaton.Api.Services;

public class AppMediaStorageService
{
    private const int ChunkSize = 256 * 1024;
    private const int UploadTimeoutMinutes = 10;
    private const int DownloadChunkTimeoutSeconds = 30;

    private readonly IHubContext<AutomatonHub> automatonHub;
    private readonly FileUploadRegistry uploadRegistry;
    private readonly FileDownloadRegistry downloadRegistry;
    private readonly MediaStorageOptions options;
    private readonly ILogger<AppMediaStorageService> logger;
    private readonly IAppDeploymentTargetResolver targetResolver;

    public AppMediaStorageService(
        IHubContext<AutomatonHub> automatonHub,
        FileUploadRegistry uploadRegistry,
        FileDownloadRegistry downloadRegistry,
        IOptions<MediaStorageOptions> options,
        ILogger<AppMediaStorageService> logger,
        IAppDeploymentTargetResolver targetResolver)
    {
        this.automatonHub = automatonHub;
        this.uploadRegistry = uploadRegistry;
        this.downloadRegistry = downloadRegistry;
        this.options = options.Value;
        this.logger = logger;
        this.targetResolver = targetResolver;
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
        return await UploadValidatedFileAsync(appId, file!, contentType, relativePath, runner, ct);
    }

    private async Task<(bool Success, int StatusCode, object Payload)> UploadValidatedFileAsync(
        Guid appId,
        IFormFile file,
        string contentType,
        string relativePath,
        Runner runner,
        CancellationToken cancellationToken)
    {
        var publicPath = AppMediaPathHelper.ToPublicPath(relativePath);
        var (sessionId, completionTask) = uploadRegistry.Register(TimeSpan.FromMinutes(UploadTimeoutMinutes), cancellationToken);

        try
        {
            using var stream = file.OpenReadStream();
            await SendUploadChunksAsync(stream, sessionId, appId, contentType, relativePath, runner, cancellationToken);

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

    private async Task SendUploadChunksAsync(
        Stream stream,
        string sessionId,
        Guid appId,
        string contentType,
        string relativePath,
        Runner runner,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[ChunkSize];
        var chunkIndex = 0;
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var chunk = new FileUploadChunk
            {
                SessionId = sessionId,
                ChunkIndex = chunkIndex++,
                Data = bytesRead == ChunkSize ? buffer : buffer[..bytesRead],
                IsLast = stream.Position >= stream.Length,
                FileName = Path.GetFileName(relativePath),
                ContentType = contentType,
                AppId = appId,
                RelativePath = relativePath,
                StorageScope = FileTransferStorageScopes.Media
            };

            await automatonHub.Clients.Client(runner.ConnectionId!)
                .SendAsync(AutomatonHubMethods.ReceiveFileChunk, chunk, cancellationToken);
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

        var metadata = await automatonHub.Clients.Client(runner.ConnectionId!)
            .InvokeAsync<AppMediaFileMetadataResponse>(
                AutomatonHubMethods.GetAppMediaFileMetadata,
                appId.ToString(),
                normalizedPath,
                ct);

        if (!metadata.Success)
        {
            return metadata.StatusCode;
        }

        var sessionId = Guid.NewGuid().ToString("N");
        var channel = downloadRegistry.Register(sessionId);
        return await StreamMediaFileAsync(appId, normalizedPath, runner, metadata, sessionId, channel, response, ct);
    }

    private async Task<int> StreamMediaFileAsync(
        Guid appId,
        string normalizedPath,
        Runner runner,
        AppMediaFileMetadataResponse metadata,
        string sessionId,
        Channel<FileChunkReadyMessage> channel,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            long offset = 0;
            response.ContentType = metadata.ContentType;
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Cache-Control"] = "public, max-age=3600";
            response.Headers["Content-Disposition"] = BuildInlineContentDispositionHeader(metadata.FileName);

            while (true)
            {
                var chunk = await RequestNextChunkAsync(
                    appId,
                    normalizedPath,
                    runner,
                    sessionId,
                    offset,
                    channel,
                    cancellationToken);
                if (chunk is null)
                    return StatusCodes.Status504GatewayTimeout;

                if (chunk.StatusCode != StatusCodes.Status200OK)
                {
                    logger.LogWarning(
                        "Media download chunk failed after preflight for app {AppId}, path {Path}, status {StatusCode}",
                        appId,
                        normalizedPath,
                        chunk.StatusCode);
                    return response.HasStarted ? StatusCodes.Status200OK : chunk.StatusCode;
                }

                if (chunk.Data.Length > 0)
                {
                    await response.Body.WriteAsync(chunk.Data, cancellationToken);
                    await response.Body.FlushAsync(cancellationToken);
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

    private async Task<FileChunkReadyMessage?> RequestNextChunkAsync(
        Guid appId,
        string normalizedPath,
        Runner runner,
        string sessionId,
        long offset,
        Channel<FileChunkReadyMessage> channel,
        CancellationToken cancellationToken)
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
            .SendAsync(AutomatonHubMethods.FetchFileChunk, request, cancellationToken);

        using var chunkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        chunkCancellation.CancelAfter(TimeSpan.FromSeconds(DownloadChunkTimeoutSeconds));
        try
        {
            return await channel.Reader.ReadAsync(chunkCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task<string?> GetRunnerErrorAsync(Guid ownerId, Guid appId, CancellationToken ct)
    {
        try
        {
            var target = await targetResolver.ResolveAsync(appId, null, ownerId, ct);
            var runner = target.ExecutionRunner!;
            return RunnerPresenceEvaluator.IsOnline(
                runner.ConnectionId,
                runner.LastHeartbeatAtUtc,
                DateTime.UtcNow)
                ? null
                : $"Runner '{runner.Name}' is not connected. Please start the runner and try again.";
        }
        catch (KeyNotFoundException)
        {
            return "The configured app or deployment target no longer exists.";
        }
        catch (UnauthorizedAccessException)
        {
            return "The runner configured for this app is not accessible by this app's owner.";
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    private async Task<Runner?> ResolveRunnerAsync(Guid ownerId, Guid appId, CancellationToken ct)
    {
        try
        {
            var target = await targetResolver.ResolveAsync(appId, null, ownerId, ct);
            var runner = target.ExecutionRunner!;
            return RunnerPresenceEvaluator.IsOnline(
                runner.ConnectionId,
                runner.LastHeartbeatAtUtc,
                DateTime.UtcNow)
                ? runner
                : null;
        }
        catch (Exception ex) when (
            ex is KeyNotFoundException or
                UnauthorizedAccessException or
                InvalidOperationException)
        {
            return null;
        }
    }

    private static string BuildInlineContentDispositionHeader(string fileName)
    {
        return $"inline; filename=\"{fileName.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
    }
}
