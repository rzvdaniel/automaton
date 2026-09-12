using System.Security.Claims;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/infrastructure-commands")]
public sealed class InfrastructureCommandsController(InfrastructureCommandCallbackService commands) : ControllerBase
{
    [HttpPost("{commandId:guid}/secrets")]
    public async Task<ActionResult<InfrastructureCommandSecretsResponse>> Secrets(Guid commandId,
        [FromBody] InfrastructureCommandSecretsRequest request, CancellationToken cancellationToken)
        => Ok(new InfrastructureCommandSecretsResponse(await commands.ResolveSecretsAsync(ApiKeyId(), commandId, request, cancellationToken)));

    [HttpPost("{commandId:guid}/result")]
    public async Task<ActionResult<InfrastructureCommandResultResponse>> Result(Guid commandId,
        [FromBody] InfrastructureCommandResultRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await commands.ApplyResultAsync(ApiKeyId(), commandId, request, cancellationToken));
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("conflicting terminal result", StringComparison.Ordinal))
        {
            return Conflict(new { message = exception.Message });
        }
    }

    private Guid ApiKeyId() => Guid.TryParse(User.FindFirst("api_key_id")?.Value, out var id)
        ? id : throw new UnauthorizedAccessException("Infrastructure commands require API-key authentication.");
}
