using Automaton.Api.Config;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public sealed class AppMediaStorageService
{
    private static readonly TimeSpan TransferDeadline = TimeSpan.FromMinutes(10);
    private readonly IRunnerFileTransferGateway transfers;
    private readonly AppFileRunnerResolver runnerResolver;
    private readonly MediaStorageOptions options;
    private readonly ILogger<AppMediaStorageService> logger;

    public AppMediaStorageService(
        IRunnerFileTransferGateway transfers,
        AppFileRunnerResolver runnerResolver,
        IOptions<MediaStorageOptions> options,
        ILogger<AppMediaStorageService> logger)
    {
        this.transfers = transfers;
        this.runnerResolver = runnerResolver;
        this.options = options.Value;
        this.logger = logger;
    }

    public async Task<(bool Success, int StatusCode, object Payload)> UploadAsync(
        Guid appId, Guid ownerId, IFormFile? file, CancellationToken token)
    {
        if (!AppMediaStoragePolicy.TryValidateUpload(file, options.MaxUploadBytes,
                out var error, out _, out var contentType))
            return (false, StatusCodes.Status400BadRequest, new { error });

        var runner = await ResolveRunnerAsync(ownerId, appId, token);
        if (runner is null)
            return (false, StatusCodes.Status400BadRequest, new { error = "No Runner is available." });

        var stats = await ReadStatsAsync(appId, ownerId, runner.Id, token);
        if (stats.Result?.Outcome != "Succeeded" || stats.Value is null)
            return (false, StatusCodes.Status502BadGateway,
                new { error = stats.Result?.Error ?? "Unable to inspect app media usage." });
        if (stats.Value.TotalBytes.GetValueOrDefault() + file!.Length > options.DefaultQuotaBytes)
            return (false, StatusCodes.Status400BadRequest,
                new { error = $"Media quota exceeded. Limit is {options.DefaultQuotaBytes} bytes." });

        var relativePath = AppMediaStoragePolicy.BuildStorageRelativePath(file.FileName, DateTime.UtcNow);
        var operationId = Guid.NewGuid();
        var command = new RunnerFileTransferCommand(RunnerFileTransferKinds.UploadMedia,
            appId, operationId.ToString("N"), Path.GetFileName(relativePath), contentType, relativePath);
        try
        {
            var (_, result) = await transfers.ExecuteAsync(operationId, runner.Id, ownerId,
                command, file, token, TransferDeadline);
            if (result.Outcome != "Succeeded" || result.FileTransferResult is not { Success: true })
                return (false, StatusCodes.Status502BadGateway,
                    new { error = result.Error ?? result.FileTransferResult?.Error ?? "Media upload failed." });
            return (true, StatusCodes.Status200OK, new
            {
                success = true,
                path = AppMediaPathHelper.ToPublicPath(relativePath),
                fileName = Path.GetFileName(relativePath),
                contentType,
                size = file.Length
            });
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return (false, StatusCodes.Status504GatewayTimeout, new { error = "Media upload timed out." });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to upload media for app {AppId}", appId);
            return (false, StatusCodes.Status502BadGateway, new { error = "Media upload failed." });
        }
    }

    public async Task<int> ProxyReadAsync(
        Guid appId, Guid ownerId, string mediaPath, HttpResponse response, CancellationToken token)
    {
        if (!TryNormalizeMediaPath(mediaPath, out var normalizedPath, out var pathError))
            return pathError;
        if (!AppMediaStoragePolicy.TryGetContentTypeForPath(normalizedPath, out _))
            return StatusCodes.Status400BadRequest;

        var runner = await ResolveRunnerAsync(ownerId, appId, token);
        if (runner is null) return StatusCodes.Status404NotFound;
        return await ProxyRunnerMediaAsync(appId, ownerId, runner.Id, normalizedPath, response, token);
    }

    private async Task<int> ProxyRunnerMediaAsync(
        Guid appId, Guid ownerId, Guid runnerId, string relativePath, HttpResponse response, CancellationToken token)
    {
        var metadataOperation = Guid.NewGuid();
        var metadataCommand = new RunnerFileTransferCommand(RunnerFileTransferKinds.ReadMediaMetadata,
            appId, metadataOperation.ToString("N"), RelativePath: relativePath);
        var metadata = await ExecuteAsync(metadataOperation, runnerId, ownerId, metadataCommand, token);
        var metadataStatus = GetTransferStatus(metadata);
        if (metadataStatus != StatusCodes.Status200OK) return metadataStatus;

        var downloadOperation = Guid.NewGuid();
        var downloadCommand = new RunnerFileTransferCommand(RunnerFileTransferKinds.DownloadMedia,
            appId, downloadOperation.ToString("N"), RelativePath: relativePath);
        try
        {
            var downloaded = await ExecuteAsync(downloadOperation, runnerId, ownerId, downloadCommand, token);
            var downloadStatus = GetTransferStatus(downloaded);
            if (downloadStatus != StatusCodes.Status200OK) return downloadStatus;

            return await StreamMediaArtifactAsync(downloadOperation, metadata!, downloaded!, response, token);
        }
        finally
        {
            await transfers.CleanupOutputAsync(downloadOperation);
        }
    }

    private async Task<int> StreamMediaArtifactAsync(
        Guid operationId, RunnerExecutionResult metadata, RunnerExecutionResult downloaded,
        HttpResponse response, CancellationToken token)
    {
        try
        {
            await using var content = transfers.OpenOutput(operationId);
            response.ContentType = downloaded.FileTransferResult!.ContentType
                ?? metadata.FileTransferResult!.ContentType ?? "application/octet-stream";
            response.ContentLength = downloaded.FileTransferResult.Length;
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Cache-Control"] = "public, max-age=3600";
            response.Headers["Content-Disposition"] = BuildInlineContentDispositionHeader(
                downloaded.FileTransferResult.FileName ?? metadata.FileTransferResult!.FileName ?? "media");
            await content.CopyToAsync(response.Body, token);
            await response.Body.FlushAsync(token);
            return StatusCodes.Status200OK;
        }
        catch (FileNotFoundException exception)
        {
            logger.LogWarning(exception, "Runner media result artifact was missing for operation {OperationId}.", operationId);
            return StatusCodes.Status502BadGateway;
        }
    }

    private static int GetTransferStatus(RunnerExecutionResult? result)
    {
        if (result?.Outcome == "Succeeded" && result.FileTransferResult is { Success: true })
            return StatusCodes.Status200OK;
        return result?.FileTransferResult?.StatusCode is int status && status is >= 400 and < 600
            ? status
            : StatusCodes.Status502BadGateway;
    }

    private static bool TryNormalizeMediaPath(string path, out string normalizedPath, out int errorStatus)
    {
        try
        {
            normalizedPath = AppMediaPathHelper.NormalizeRelativePath(path);
            errorStatus = StatusCodes.Status200OK;
            return true;
        }
        catch (ArgumentException)
        {
            normalizedPath = string.Empty;
            errorStatus = StatusCodes.Status400BadRequest;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            normalizedPath = string.Empty;
            errorStatus = StatusCodes.Status403Forbidden;
            return false;
        }
    }

    private async Task<(RunnerExecutionResult? Result, RunnerFileTransferResult? Value)> ReadStatsAsync(
        Guid appId, Guid ownerId, Guid runnerId, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var command = new RunnerFileTransferCommand(RunnerFileTransferKinds.ReadMediaStats, appId, id.ToString("N"));
        var result = await ExecuteAsync(id, runnerId, ownerId, command, token);
        return (result, result?.FileTransferResult);
    }

    private async Task<RunnerExecutionResult?> ExecuteAsync(
        Guid id, Guid runnerId, Guid ownerId, RunnerFileTransferCommand command, CancellationToken token)
    {
        try
        {
            var (_, result) = await transfers.ExecuteAsync(id, runnerId, ownerId, command, null, token, TransferDeadline);
            return result;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return new RunnerExecutionResult { Error = "Runner file transfer timed out." };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Runner file transfer failed for app {AppId}, operation {OperationId}.", command.AppId, id);
            return new RunnerExecutionResult { Error = "Runner file transfer failed." };
        }
    }

    private async Task<Automaton.Api.Entities.Runner?> ResolveRunnerAsync(Guid ownerId, Guid appId, CancellationToken token)
    {
        var error = await runnerResolver.GetUnavailableErrorAsync(ownerId, appId, token);
        return error is null ? await runnerResolver.ResolveOnlineRunnerAsync(ownerId, appId, token) : null;
    }

    private static string BuildInlineContentDispositionHeader(string fileName)
        => $"inline; filename=\"{fileName.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
}
