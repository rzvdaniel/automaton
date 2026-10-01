using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Authorize(Policy = "AllowApiKey")]
[Route("api/runner-executions")]
public sealed class RunnerExecutionsController(
    RunnerExecutionIdentityService identity, HostedRunnerBrokerProvisioner broker, RunnerExecutionOperationService operations) : ControllerBase
{
    [HttpGet("configuration")]
    public async Task<ActionResult<HostedRunnerBrokerConfiguration>> Configuration(CancellationToken token)
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
    }

    [HttpGet("{id:guid}/status")]
    public async Task<ActionResult<RunnerExecutionAssignment>> Status(Guid id, CancellationToken token)
    {
        Response.Headers.CacheControl = "no-store";
        if (RunnerId() == Guid.Empty) return Forbid();
        try { return Ok(await operations.AssignmentAsync(KeyId(), id, token, includeRequest: false, authenticatedUserId: ActorId(), runnerId: RunnerId())); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    private Guid RunnerId() => Guid.TryParse(Request.Headers["X-Runner-Id"], out var id) ? id : Guid.Empty;
    private Guid ActorId() => Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
    private Guid KeyId() => Guid.TryParse(User.FindFirst("api_key_id")?.Value, out var id) ? id : Guid.Empty;
}
