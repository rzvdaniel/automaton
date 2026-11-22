using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("runtime-tunnel/apps/{appId:guid}/targets/{deploymentTargetId:guid}/{**path}", Order = -20)]
[AllowAnonymous]
public sealed class RuntimeTunnelController(
    RuntimeTunnelService runtimeTunnelService,
    RuntimeTunnelRequestSender requestSender) : ControllerBase
{
    [AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS")]
    // No action parameters on purpose: with any parameter MVC runs its value providers first, and the form provider
    // reads and consumes an application/x-www-form-urlencoded body before it can be forwarded to the app.
    public async Task ProxyAsync()
    {
        var cancellationToken = HttpContext.RequestAborted;
        var appId = Guid.Parse((string)RouteData.Values["appId"]!);
        var deploymentTargetId = Guid.Parse((string)RouteData.Values["deploymentTargetId"]!);
        var path = RouteData.Values["path"] as string ?? string.Empty;

        var resolved = await runtimeTunnelService.ResolveAsync(appId, deploymentTargetId, Request, path, cancellationToken);
        if (resolved.Failure is { } failure)
        {
            await failure.WriteAsync(Response, cancellationToken);
            return;
        }

        await requestSender.ProxyAsync(HttpContext, appId, deploymentTargetId, resolved.Runner!, path, resolved.StaticSite);
    }
}
