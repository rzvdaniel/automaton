using System.Net;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Internal;

[ApiController]
[Route("internal/runners")]
[AllowAnonymous]
public class RunnerDrainController : ControllerBase
{
    private readonly RunnerService runnerService;

    public RunnerDrainController(RunnerService runnerService)
    {
        this.runnerService = runnerService;
    }

    [HttpPost("reconnect-when-idle")]
    public async Task<ActionResult<ReconnectWhenIdleResponse>> RequestReconnectWhenIdle(
        [FromBody] ReconnectWhenIdleRequest? request,
        CancellationToken cancellationToken)
    {
        if (!IsLoopback(HttpContext.Connection.RemoteIpAddress))
        {
            return NotFound();
        }

        var reason = string.IsNullOrWhiteSpace(request?.Reason)
            ? "API deploy cutover complete. Reconnect to the new live API instance when idle."
            : request!.Reason.Trim();

        var signaled = await runnerService.RequestConnectedRunnersReconnectWhenIdleAsync(reason, cancellationToken);
        return Ok(new ReconnectWhenIdleResponse
        {
            SignaledRunnerCount = signaled,
            Reason = reason
        });
    }

    [HttpGet("drain-status")]
    public async Task<ActionResult<RunnerDrainStatus>> GetDrainStatus(CancellationToken cancellationToken)
    {
        if (!IsLoopback(HttpContext.Connection.RemoteIpAddress))
        {
            return NotFound();
        }

        return Ok(await runnerService.GetReconnectDrainStatusAsync(cancellationToken));
    }

    private static bool IsLoopback(IPAddress? address)
    {
        return address != null && IPAddress.IsLoopback(address);
    }
}

public record ReconnectWhenIdleRequest
{
    public string? Reason { get; init; }
}

public record ReconnectWhenIdleResponse
{
    public int SignaledRunnerCount { get; init; }
    public string Reason { get; init; } = string.Empty;
}
