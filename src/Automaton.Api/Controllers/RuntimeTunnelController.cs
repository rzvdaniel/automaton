using Automaton.Api.Config;
using Automaton.Api.Services;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("runtime-tunnel/apps/{appId:guid}/targets/{deploymentTargetId:guid}/{**path}", Order = -20)]
[AllowAnonymous]
public sealed class RuntimeTunnelController(
    RuntimeTunnelService runtimeTunnelService,
    RuntimeTunnelWebSocketService webSocketService,
    IOptions<RuntimeTunnelConfig> tunnelOptions) : ControllerBase
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
        TunnelResponse? tunnelResponse;
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            tunnelResponse = await webSocketService.ProxyAsync(
                appId, deploymentTargetId, HttpContext, path ?? string.Empty, cancellationToken);
            if (tunnelResponse is null) return;
        }
        else
        {
            tunnelResponse = await runtimeTunnelService.ForwardAsync(
                appId,
                deploymentTargetId,
                Request,
                path ?? string.Empty,
                cancellationToken);
        }

        Response.StatusCode = tunnelResponse.StatusCode;
        foreach (var (name, values) in tunnelResponse.HeaderValues)
        {
            if (!IsHopByHopResponseHeader(name))
                Response.Headers[name] = values;
        }

        if (tunnelResponse.BodyStream is { } bodyStream)
        {
            await WriteStreamAsync(bodyStream, cancellationToken);
            return;
        }

        if (tunnelResponse.BodyBytes is { Length: > 0 })
            await Response.Body.WriteAsync(tunnelResponse.BodyBytes, cancellationToken);
    }

    // Each chunk is flushed as it arrives so Server-Sent Events reach the browser live. Chunks the browser has
    // written are acknowledged so the Runner keeps a bounded number in flight; a stalled stream is cut off.
    private async Task WriteStreamAsync(TunnelBodyStream bodyStream, CancellationToken cancellationToken)
    {
        using var lease = bodyStream;
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        Response.Headers["X-Accel-Buffering"] = "no";
        var idleTimeout = TimeSpan.FromSeconds(tunnelOptions.Value.StreamIdleTimeoutSeconds);
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idle.CancelAfter(idleTimeout);
        try
        {
            await Response.Body.FlushAsync(idle.Token);
            await foreach (var chunk in bodyStream.Reader.ReadAllAsync(idle.Token))
            {
                await Response.Body.WriteAsync(chunk, idle.Token);
                await Response.Body.FlushAsync(idle.Token);
                idle.CancelAfter(idleTimeout);
                await bodyStream.AcknowledgeAsync();
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException)
        {
            // Browser left, idle timeout, or the Runner stopped mid-body: end the connection so the client sees a truncated stream.
            await bodyStream.CancelAsync();
            HttpContext.Abort();
        }
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
