using System.Net.WebSockets;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Authorization;

namespace Automaton.Api.Services;

/// <summary>
/// Where a Runner opens its tunnel connections (outbound only, so a Runner needs no inbound port). The accepted
/// WebSocket becomes an HTTP/2 connection the API sends app requests over.
/// </summary>
public static class RunnerTunnelEndpoint
{
    public const string Route = "/api/runner-tunnel/connect";
    internal const int MaxConnectionsPerRunner = 8;
    private const string RunnerIdHeader = "RunnerId";

    public static void MapRunnerTunnel(this IEndpointRouteBuilder routes)
        => routes.Map(Route, HandleAsync).RequireAuthorization(new AuthorizeAttribute { Policy = "AllowApiKey" });

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        RunnerTunnelRegistry registry,
        RunnerExecutionIdentityService runnerIdentity,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Automaton.Api.RunnerTunnel");
        if (!context.WebSockets.IsWebSocketRequest)
            return Results.BadRequest("A WebSocket upgrade is required.");
        if (!Guid.TryParse(context.Request.Headers[RunnerIdHeader].ToString(), out var runnerId))
            return Results.BadRequest("The RunnerId header is missing.");

        var userId = Guid.TryParse(
            context.User.Claims.FirstOrDefault(claim => claim.Type is "uid" or System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            out var parsedUserId) ? parsedUserId : Guid.Empty;
        var apiKeyId = Guid.TryParse(context.User.FindFirst("api_key_id")?.Value, out var parsedKeyId) ? parsedKeyId : Guid.Empty;
        try { await runnerIdentity.EnsureAsync(apiKeyId, runnerId, userId, context.RequestAborted); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Rejected tunnel connection for runner {RunnerId}", runnerId);
            return Results.Unauthorized();
        }

        if (registry.ConnectionCount(runnerId) >= MaxConnectionsPerRunner)
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await using var stream = new TunnelConnectionStream(
            WebSocketStream.Create(socket, WebSocketMessageType.Binary, ownsWebSocket: true));
        using var registration = registry.Add(runnerId, stream, out var closed, MaxConnectionsPerRunner);
        if (registration is null)
        {
            // Lost a race with other connections of the same Runner: tell it to back off.
            await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "too many tunnel connections", CancellationToken.None);
            return Results.Empty;
        }
        logger.LogInformation("Runner {RunnerId} opened a tunnel connection ({Count} open)", runnerId, registry.ConnectionCount(runnerId));
        await Task.WhenAny(closed, Task.Delay(Timeout.Infinite, context.RequestAborted)).ConfigureAwait(false);
        logger.LogInformation("Runner {RunnerId} tunnel connection ended", runnerId);
        return Results.Empty;
    }
}
