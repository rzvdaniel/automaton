using System.Net;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers.Internal;

[ApiController]
[Route("internal/runners")]
[AllowAnonymous]
public sealed class RunnerTunnelReconnectController(RunnerTunnelReconnectService reconnectService) : ControllerBase
{
    [HttpPost("reconnect-runtime-tunnels")]
    public async Task<ActionResult<RunnerTunnelReconnectResult>> RequestReconnect(
        [FromBody] RunnerTunnelReconnectRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsLoopback(HttpContext.Connection.RemoteIpAddress))
            return NotFound();

        if (request.TargetApiColor is not ("blue" or "green"))
            return BadRequest("Target API color must be blue or green.");

        var result = await reconnectService.RequestForApiColorAsync(
            request.TargetApiColor, request.IncludeRunnersAlreadyOnTargetColor, cancellationToken);
        if (result.FailedCount > 0)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, result);

        return Accepted(result);
    }

    private static bool IsLoopback(IPAddress? address)
        => address is not null && IPAddress.IsLoopback(address);
}

public sealed record RunnerTunnelReconnectRequest(string TargetApiColor, bool IncludeRunnersAlreadyOnTargetColor = false);
