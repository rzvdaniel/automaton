using System.Security.Claims;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/hosted-runner-commands/operations")]
public sealed class HostedRunnerDeploymentOperationsController(
    HostedRunnerDeploymentCommandService commands,
    HostedRunnerDeploymentResultService results) : ControllerBase
{
    [HttpGet("{operationId:guid}/package")]
    public async Task<IActionResult> Package(
        Guid operationId,
        [FromQuery] Guid attemptId,
        CancellationToken cancellationToken)
    {
        var stream = await commands.OpenPackageAsync(ApiKeyId(), operationId, attemptId, cancellationToken);
        if (stream is null)
            return NotFound();
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return File(stream, "application/zip", enableRangeProcessing: false);
    }

    [HttpGet("{operationId:guid}/status")]
    public async Task<ActionResult<HostedRunnerCommandResultResponse>> Status(
        Guid operationId,
        [FromQuery] Guid attemptId,
        CancellationToken cancellationToken)
        => Ok(new HostedRunnerCommandResultResponse(await commands.GetCommandDispositionAsync(
            ApiKeyId(), operationId, attemptId, cancellationToken)));

    [HttpPost("{operationId:guid}/result")]
    public async Task<ActionResult<HostedRunnerCommandResultResponse>> Result(
        Guid operationId,
        [FromBody] HostedRunnerDeploymentResultRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await results.ApplyAsync(ApiKeyId(), operationId, request, cancellationToken);
        return Ok(new HostedRunnerCommandResultResponse(outcome));
    }

    [HttpPost("{operationId:guid}/reject")]
    public async Task<ActionResult<HostedRunnerCommandResultResponse>> Reject(
        Guid operationId,
        [FromBody] HostedRunnerDeploymentCommandRejectionRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = await results.RejectAsync(ApiKeyId(), operationId, request, cancellationToken);
        return Ok(new HostedRunnerCommandResultResponse(outcome));
    }

    private Guid ApiKeyId() => Guid.TryParse(User.FindFirst("api_key_id")?.Value, out var id)
        ? id
        : throw new UnauthorizedAccessException("Hosted Runner commands require API-key authentication.");
}

public sealed record HostedRunnerCommandResultResponse(string Outcome);
