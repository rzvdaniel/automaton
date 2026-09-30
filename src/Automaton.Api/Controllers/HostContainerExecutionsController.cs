using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/host-container-executions")]
public sealed class HostContainerExecutionsController(
    HostContainerRelayService relay,
    HostContainerExecutionOperationService operations) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<HostContainerExecutionOperationResponse>> Submit(
        [FromBody] HostContainerExecutionRequest request,
        CancellationToken cancellationToken)
        => Ok(await relay.SubmitAsync(ApiKeyId(), request, cancellationToken));

    [HttpGet("{operationId:guid}/result")]
    public async Task<ActionResult<HostContainerExecutionOperationResponse>> Result(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var response = await operations.GetResultAsync(ApiKeyId(), operationId, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    [HttpGet("{operationId:guid}/disposition")]
    public async Task<ActionResult<HostContainerCommandDispositionResponse>> Disposition(
        Guid operationId,
        CancellationToken cancellationToken)
        => Ok(new HostContainerCommandDispositionResponse(await operations.GetCommandDispositionAsync(
            ApiKeyId(), operationId, cancellationToken)));

    [HttpPost("{operationId:guid}/result")]
    public async Task<ActionResult<HostContainerCommandDispositionResponse>> ApplyResult(
        Guid operationId,
        [FromBody] HostContainerExecutionResultRequest request,
        CancellationToken cancellationToken)
        => Ok(new HostContainerCommandDispositionResponse(await operations.ApplyResultAsync(
            ApiKeyId(), operationId, request, cancellationToken)));

    private Guid ApiKeyId() => Guid.TryParse(User.FindFirst("api_key_id")?.Value, out var id)
        ? id
        : throw new UnauthorizedAccessException("Host-container operations require API-key authentication.");
}
