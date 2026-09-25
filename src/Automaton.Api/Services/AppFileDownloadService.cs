using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Api.Services;

public sealed class AppFileDownloadService(
    IHubContext<AutomatonHub> automatonHub,
    FileDownloadRegistry downloadRegistry,
    AppFileRunnerResolver runnerResolver,
    ILogger<Controllers.AppFilesController> logger)
{
    private const int ChunkSize = 256 * 1024;
    private const int DownloadChunkTimeoutSeconds = 30;

    internal async Task DownloadAsync(
        Guid appId,
        Guid ownerId,
        string sessionId,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var runner = await runnerResolver.ResolveOnlineRunnerAsync(ownerId, appId, cancellationToken);
        if (runner is null)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "No runner available." }, cancellationToken);
            return;
        }

        var channel = downloadRegistry.Register(sessionId);
        logger.LogDebug(
            "[AppFilesController] Download started: SessionId={SessionId} AppId={AppId} Runner={Runner}",
            sessionId,
            appId,
            runner.Name);

        try
        {
            await StreamDownloadAsync(runner, sessionId, channel, response, cancellationToken);
            logger.LogDebug("[AppFilesController] Download complete: SessionId={SessionId}", sessionId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "[AppFilesController] Download failed: SessionId={SessionId} AppId={AppId}",
                sessionId,
                appId);
        }
        finally
        {
            downloadRegistry.Complete(sessionId);
        }
    }

    private async Task StreamDownloadAsync(
        Runner runner,
        string sessionId,
        System.Threading.Channels.Channel<FileChunkReadyMessage> channel,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        long offset = 0;
        var headerWritten = false;
        while (true)
        {
            var chunk = await ReadNextChunkAsync(runner, sessionId, offset, channel, cancellationToken);
            if (chunk is null)
            {
                logger.LogWarning("[AppFilesController] Download chunk timeout: SessionId={SessionId}", sessionId);
                break;
            }

            if (!headerWritten)
            {
                response.ContentType = chunk.ContentType;
                response.Headers.ContentDisposition =
                    $"attachment; filename=\"{Uri.EscapeDataString(chunk.FileName)}\"";
                headerWritten = true;
            }

            await response.Body.WriteAsync(chunk.Data, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
            if (chunk.IsLast)
                break;

            offset += chunk.Data.Length;
        }
    }

    private async Task<FileChunkReadyMessage?> ReadNextChunkAsync(
        Runner runner,
        string sessionId,
        long offset,
        System.Threading.Channels.Channel<FileChunkReadyMessage> channel,
        CancellationToken cancellationToken)
    {
        var request = new FetchFileChunkRequest
        {
            SessionId = sessionId,
            Offset = offset,
            ChunkSize = ChunkSize
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
}
