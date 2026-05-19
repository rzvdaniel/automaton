using System.Net;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Studio.Server.Controllers.Internal;

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
        if (!IsPrivateOrLoopback(HttpContext.Connection.RemoteIpAddress))
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

    private static bool IsPrivateOrLoopback(IPAddress? address)
    {
        if (address == null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168);
        }

        return false;
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
