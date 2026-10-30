using Automaton.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Services;

public sealed class AppFileUploadService(
    IRunnerFileTransferGateway transfers,
    AppFileRunnerResolver runnerResolver,
    ILogger<AppFileUploadService> logger)
{
    private static readonly TimeSpan UploadDeadline = TimeSpan.FromMinutes(10);

    internal async Task<IActionResult> UploadAsync(
        Guid appId, Guid ownerId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return new BadRequestObjectResult(new { error = "No file provided." });
        var runnerError = await runnerResolver.GetUnavailableErrorAsync(ownerId, appId, cancellationToken);
        if (runnerError is not null)
            return new BadRequestObjectResult(new { error = runnerError });
        var runner = await runnerResolver.ResolveOnlineRunnerAsync(ownerId, appId, cancellationToken);
        if (runner is null)
            return new BadRequestObjectResult(new { error = "Runner is no longer available. Please try again." });

        var operationId = Guid.NewGuid();
        var sessionId = operationId.ToString("N");
        var command = new RunnerFileTransferCommand(
            RunnerFileTransferKinds.UploadAppFile, appId, sessionId,
            SanitizeFileName(file.FileName), file.ContentType ?? "application/octet-stream");
        try
        {
            var (_, result) = await transfers.ExecuteAsync(operationId, runner.Id, ownerId, command, file,
                cancellationToken, UploadDeadline);
            if (result.Outcome == "Succeeded" && result.FileTransferResult?.Success == true)
                return new OkObjectResult(new { session_id = sessionId });
            return new ObjectResult(new { error = result.Error ?? result.FileTransferResult?.Error ?? "Runner upload failed." })
                { StatusCode = StatusCodes.Status502BadGateway };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ObjectResult(new { error = "Runner upload timed out." }) { StatusCode = StatusCodes.Status504GatewayTimeout };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Runner upload failed for app {AppId}, operation {OperationId}.", appId, operationId);
            return new ObjectResult(new { error = "Runner upload failed." }) { StatusCode = StatusCodes.Status502BadGateway };
        }
    }

    private static string SanitizeFileName(string? raw)
    {
        var name = Path.GetFileName((raw ?? string.Empty).Replace('\0', '_').Trim());
        return string.IsNullOrWhiteSpace(name) ? "download" : name;
    }
}
