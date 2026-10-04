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
    public async Task<ActionResult<RunnerTunnelReconnectResult>> RequestReconnect(CancellationToken cancellationToken)
    {
        if (!IsLoopback(HttpContext.Connection.RemoteIpAddress))
            return NotFound();

        var result = await reconnectService.RequestForPreviousInstancesAsync(cancellationToken);
        if (result.FailedCount > 0)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, result);

        return Accepted(result);
    }

    private static bool IsLoopback(IPAddress? address)
        => address is not null && IPAddress.IsLoopback(address);
}
