using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("runtime-tunnel/apps/{appId:guid}/targets/{deploymentTargetId:guid}/{**path}")]
[AllowAnonymous]
public sealed class RuntimeTunnelController(RuntimeTunnelService runtimeTunnelService) : ControllerBase
{
    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS")]
    public async Task ProxyAsync(Guid appId, Guid deploymentTargetId, string? path, CancellationToken cancellationToken)
    {
        var tunnelResponse = await runtimeTunnelService.ForwardAsync(
            appId,
            deploymentTargetId,
            Request,
            path ?? string.Empty,
            cancellationToken);

        Response.StatusCode = tunnelResponse.StatusCode;
        foreach (var (name, values) in tunnelResponse.HeaderValues)
        {
            if (!string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                Response.Headers[name] = values;
        }

        if (tunnelResponse.BodyBytes is { Length: > 0 })
            await Response.Body.WriteAsync(tunnelResponse.BodyBytes, cancellationToken);
    }
}
