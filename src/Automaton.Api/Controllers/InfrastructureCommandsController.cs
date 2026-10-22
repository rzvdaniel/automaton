using Automaton.Api.Models;
using System.Security.Claims;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/infrastructure-commands")]
public sealed class InfrastructureCommandsController(
    InfrastructureHostValidationResultService hostValidation) : ControllerBase
{
    [HttpPost("{commandId:guid}/result")]
    public async Task<ActionResult<InfrastructureCommandResultResponse>> Result(Guid commandId,
        [FromBody] InfrastructureCommandResultRequest request, CancellationToken cancellationToken)
    {
        if (request.ActionKey != HostValidationCommand.ActionKey)
            return Forbid();
        try
        {
            return Ok(await hostValidation.ApplyAsync(ApiKeyId(), commandId, request, cancellationToken));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception) { return BadRequest(exception.Message); }
        catch (System.Text.Json.JsonException) { return BadRequest("Invalid validation result JSON."); }
    }

    private Guid ApiKeyId() => Guid.TryParse(User.FindFirst("api_key_id")?.Value, out var id)
        ? id : throw new UnauthorizedAccessException("Infrastructure commands require API-key authentication.");
}
