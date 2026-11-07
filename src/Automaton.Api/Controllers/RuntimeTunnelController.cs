using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("runtime-tunnel/apps/{appId:guid}/targets/{deploymentTargetId:guid}/{**path}", Order = -20)]
[AllowAnonymous]
public sealed class RuntimeTunnelController(RuntimeTunnelService runtimeTunnelService) : ControllerBase
{
    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS")]
    // No action parameters on purpose: with any parameter MVC runs its value providers first, and the form provider
    // reads and consumes an application/x-www-form-urlencoded body before it can be forwarded to the app.
    public async Task ProxyAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        var appId = Guid.Parse((string)RouteData.Values["appId"]!);
        var deploymentTargetId = Guid.Parse((string)RouteData.Values["deploymentTargetId"]!);
        var path = RouteData.Values["path"] as string;
        var tunnelResponse = await runtimeTunnelService.ForwardAsync(
            appId,
            deploymentTargetId,
            Request,
            path ?? string.Empty,
            cancellationToken);

        Response.StatusCode = tunnelResponse.StatusCode;
        foreach (var (name, values) in tunnelResponse.HeaderValues)
        {
            if (!IsHopByHopResponseHeader(name))
                Response.Headers[name] = values;
        }

        if (tunnelResponse.BodyBytes is { Length: > 0 })
            await Response.Body.WriteAsync(tunnelResponse.BodyBytes, cancellationToken);
    }

    private static bool IsHopByHopResponseHeader(string name)
        => string.Equals(name, "Connection", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Keep-Alive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Proxy-Authenticate", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "TE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Trailer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Upgrade", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase);
}
