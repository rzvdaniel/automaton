using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Automaton.Api.Services;

public sealed class AppFileUploadService(
    IHubContext<AutomatonHub> automatonHub,
    FileUploadRegistry uploadRegistry,
    AppFileRunnerResolver runnerResolver,
    ILogger<Controllers.AppFilesController> logger)
{
    private const int ChunkSize = 256 * 1024;
    private const int UploadTimeoutMinutes = 10;

    internal async Task<IActionResult> UploadAsync(Guid appId, Guid ownerId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return new BadRequestObjectResult(new { error = "No file provided." });

        var runnerError = await runnerResolver.GetUnavailableErrorAsync(ownerId, appId, cancellationToken);
        if (runnerError is not null)
            return new BadRequestObjectResult(new { error = runnerError });

        var runner = await runnerResolver.ResolveOnlineRunnerAsync(ownerId, appId, cancellationToken);
        if (runner is null)
            return new BadRequestObjectResult(new { error = "Runner is no longer available. Please try again." });

        var (sessionId, completionTask) = uploadRegistry.Register(
            TimeSpan.FromMinutes(UploadTimeoutMinutes),
            cancellationToken);
        var fileName = SanitizeFileName(file.FileName);
        var contentType = file.ContentType ?? "application/octet-stream";

        logger.LogDebug(
            "[AppFilesController] Upload started: SessionId={SessionId} AppId={AppId} Runner={Runner} File={File} Size={Size}",
            sessionId,
            appId,
            runner.Name,
            fileName,
            file.Length);

        try
        {
            using var stream = file.OpenReadStream();
            var chunkCount = await SendChunksAsync(stream, sessionId, fileName, contentType, runner, cancellationToken);
            await completionTask;

            logger.LogDebug(
                "[AppFilesController] Upload complete: SessionId={SessionId} Chunks={ChunkCount}",
                sessionId,
                chunkCount);
            return new OkObjectResult(new { session_id = sessionId });
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "[AppFilesController] Upload failed: SessionId={SessionId} AppId={AppId}",
                sessionId,
                appId);
            return new ObjectResult(new { error = exception.Message }) { StatusCode = 500 };
        }
    }

    private async Task<int> SendChunksAsync(
        Stream stream,
        string sessionId,
        string fileName,
        string contentType,
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
                FileName = fileName,
                ContentType = contentType
            };

            await automatonHub.Clients.Client(runner.ConnectionId!)
                .SendAsync(AutomatonHubMethods.ReceiveFileChunk, chunk, cancellationToken);
        }

        return chunkIndex;
    }

    private static string SanitizeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "download";
        var name = Path.GetFileName(raw.Replace('\0', '_').Trim());
        return string.IsNullOrWhiteSpace(name) ? "download" : name;
    }
}
