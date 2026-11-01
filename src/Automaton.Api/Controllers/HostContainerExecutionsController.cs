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
    {
        try { return Ok(await relay.SubmitAsync(ApiKeyId(), request, cancellationToken)); }
        // Permanent: the Runner must not retry a reused operation ID with a different request.
        catch (OperationIdentityConflictException exception) { return Conflict(new { message = exception.Message }); }
    }

    [HttpGet("{operationId:guid}/result")]
    public async Task<ActionResult<HostContainerExecutionOperationResponse>> Result(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var response = await operations.GetResultAsync(ApiKeyId(), operationId, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    private Guid ApiKeyId() => Guid.TryParse(User.FindFirst("api_key_id")?.Value, out var id)
        ? id
        : throw new UnauthorizedAccessException("Host-container operations require API-key authentication.");
}
