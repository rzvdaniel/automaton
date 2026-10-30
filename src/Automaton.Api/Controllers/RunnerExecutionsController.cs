using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/runner-executions")]
public sealed class RunnerExecutionsController(
    RunnerExecutionIdentityService identity, HostedRunnerBrokerProvisioner broker,
    RunnerExecutionOperationService operations, RunnerExecutionArtifactService? artifacts = null) : ControllerBase
{
    [HttpGet("configuration")]
    public async Task<ActionResult<RunnerBrokerConfiguration>> Configuration(CancellationToken token)
    {
        var runnerId = RunnerId();
        try { await identity.EnsureAsync(KeyId(), runnerId, ActorId(), token); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        Response.Headers.CacheControl = "no-store";
        return Ok(await broker.ProvisionExecutionAsync(runnerId, token));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RunnerExecutionAssignment>> Assignment(Guid id, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        if (RunnerId() == Guid.Empty) return Forbid();
        try { return Ok(await operations.AssignmentAsync(KeyId(), id, token, authenticatedUserId: ActorId(), runnerId: RunnerId())); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("{id:guid}/result")]
    public async Task<ActionResult<RunnerExecutionReceipt>> Result(Guid id, RunnerExecutionResult result, CancellationToken token)
    {
        if (RunnerId() == Guid.Empty) return Forbid();
        try { return Ok(await operations.ApplyAsync(KeyId(), id, result, token, ActorId(), RunnerId())); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException exception) { return BadRequest(exception.Message); }
        catch (System.Text.Json.JsonException) { return BadRequest("Malformed Action result."); }
    }

    [HttpGet("{id:guid}/status")]
    public async Task<ActionResult<RunnerExecutionAssignment>> Status(Guid id, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        if (RunnerId() == Guid.Empty) return Forbid();
        try { return Ok(await operations.AssignmentAsync(KeyId(), id, token, includeRequest: false, authenticatedUserId: ActorId(), runnerId: RunnerId())); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpGet("{id:guid}/input")]
    public async Task<IActionResult> Input(Guid id, CancellationToken token)
    {
        try
        {
            var stream = await artifacts!.OpenInputAsync(KeyId(), RunnerId(), ActorId(), id, token);
            if (stream is null) return NotFound();
            Response.Headers.CacheControl = "no-store";
            return File(stream, "application/octet-stream", enableRangeProcessing: false);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception) { return Conflict(exception.Message); }
        catch (FileNotFoundException) { return NotFound(); }
    }

    [HttpPost("{id:guid}/artifact")]
    [RequestSizeLimit(RunnerFileTransferBounds.MaximumBytes)]
    public async Task<IActionResult> Artifact(Guid id, CancellationToken token)
    {
        try
        {
            await artifacts!.StoreOutputAsync(KeyId(), RunnerId(), ActorId(), id,
                Request.Body, Request.ContentLength, token);
            return Ok();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (InvalidOperationException exception) { return Conflict(exception.Message); }
        catch (InvalidDataException exception)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = exception.Message });
        }
    }

    private Guid RunnerId() => Guid.TryParse(Request.Headers["X-Runner-Id"], out var id) ? id : Guid.Empty;
    private Guid ActorId() => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
    private Guid KeyId() => Guid.TryParse(User.FindFirst("api_key_id")?.Value, out var id) ? id : Guid.Empty;
}
