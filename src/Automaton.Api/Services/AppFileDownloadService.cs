using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Automaton.Api.Services;

public sealed class AppFileDownloadService(
    IRunnerFileTransferGateway transfers,
    AppFileRunnerResolver runnerResolver,
    ILogger<AppFileDownloadService> logger)
{
    private static readonly TimeSpan DownloadDeadline = TimeSpan.FromMinutes(10);

    internal async Task DownloadAsync(
        Guid appId, Guid ownerId, string sessionId, HttpResponse response, CancellationToken cancellationToken)
    {
        var runner = await runnerResolver.ResolveOnlineRunnerAsync(ownerId, appId, cancellationToken);
        if (runner is null)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "No runner available." }, cancellationToken);
            return;
        }

        var operationId = Guid.NewGuid();
        try
        {
            var command = new RunnerFileTransferCommand(
                RunnerFileTransferKinds.DownloadAppFile, appId, sessionId);
            var (_, result) = await transfers.ExecuteAsync(operationId, runner.Id, ownerId, command, null,
                cancellationToken, DownloadDeadline);
            if (result.Outcome != "Succeeded" || result.FileTransferResult is not { Success: true } artifact)
            {
                response.StatusCode = result.FileTransferResult?.StatusCode is >= 400 and < 600
                    ? result.FileTransferResult.StatusCode.Value
                    : StatusCodes.Status502BadGateway;
                await response.WriteAsJsonAsync(new { error = result.Error ?? result.FileTransferResult?.Error ?? "Runner download failed." }, cancellationToken);
                return;
            }

            await using var stream = transfers.OpenOutput(operationId);
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = artifact.ContentType ?? "application/octet-stream";
            response.ContentLength = artifact.Length;
            response.Headers.ContentDisposition = $"attachment; filename=\"{Uri.EscapeDataString(artifact.FileName ?? "download")}\"";
            await stream.CopyToAsync(response.Body, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!response.HasStarted)
            {
                response.StatusCode = StatusCodes.Status504GatewayTimeout;
                await response.WriteAsJsonAsync(new { error = "Runner download timed out." }, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Runner download failed for app {AppId}, operation {OperationId}.", appId, operationId);
            if (!response.HasStarted)
            {
                response.StatusCode = StatusCodes.Status502BadGateway;
                await response.WriteAsJsonAsync(new { error = "Runner download failed." }, cancellationToken);
            }
        }
        finally
        {
            await transfers.CleanupOutputAsync(operationId);
        }
    }
}
