using Automaton.Api.Config;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public sealed class RuntimeTunnelRequestSender(
    IHubContext<AutomatonHub> hub,
    TunnelRequestRegistry registry,
    IOptions<RuntimeTunnelConfig> options,
    ILogger<RuntimeTunnelRequestSender> logger)
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host", "Content-Length"
    };

    private readonly RuntimeTunnelConfig config = options.Value;

    public async Task<TunnelResponse> ForwardAsync(
        Guid appId,
        Guid deploymentTargetId,
        Runner runner,
        HttpRequest request,
        string path,
        CancellationToken cancellationToken,
        bool staticSite = false)
    {
        if (request.ContentLength is > 0 && request.ContentLength > config.MaxBodyBytes)
        {
            return Failure(StatusCodes.Status413PayloadTooLarge, "The request body exceeds the runtime tunnel limit.");
        }

        var (body, bodyTooLarge) = await ReadBodyAsync(request, config.MaxBodyBytes, cancellationToken);
        if (bodyTooLarge)
        {
            return Failure(StatusCodes.Status413PayloadTooLarge, "The request body exceeds the runtime tunnel limit.");
        }

        var (requestId, responseTask) = registry.Register(
            TimeSpan.FromSeconds(config.TimeoutSeconds), cancellationToken);
        var tunnelRequest = CreateTunnelRequest(requestId, appId, deploymentTargetId, request, path, body, staticSite);

        return await SendAndWaitAsync(
            tunnelRequest, responseTask, runner, appId, deploymentTargetId, request.Host.Host, path, cancellationToken);
    }

    private async Task<TunnelResponse> SendAndWaitAsync(
        TunnelRequest tunnelRequest,
        Task<TunnelResponse> responseTask,
        Runner runner,
        Guid appId,
        Guid deploymentTargetId,
        string host,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Client(runner.ConnectionId!)
                .SendAsync(AutomatonHubMethods.TunnelRequest, tunnelRequest, cancellationToken);
            var response = await responseTask;
            if (response.StatusCode >= StatusCodes.Status400BadRequest)
            {
                logger.LogWarning(
                    "Runner tunnel response: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} StatusCode={StatusCode}",
                    appId,
                    deploymentTargetId,
                    runner.Id,
                    host,
                    path,
                    response.StatusCode);
            }

            return response;
        }
        catch (TimeoutException)
        {
            logger.LogWarning(
                "Runtime tunnel timed out for app {AppId}, target {TargetId}, runner {RunnerId}",
                appId,
                deploymentTargetId,
                runner.Id);
            return Failure(StatusCodes.Status504GatewayTimeout, "The Runner did not respond in time.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Runtime tunnel failed for app {AppId}, target {TargetId}, runner {RunnerId}",
                appId,
                deploymentTargetId,
                runner.Id);
            return Failure(StatusCodes.Status502BadGateway, "The Runner runtime tunnel is unavailable.");
        }
    }

    private static TunnelRequest CreateTunnelRequest(
        string requestId,
        Guid appId,
        Guid deploymentTargetId,
        HttpRequest request,
        string path,
        byte[]? body,
        bool staticSite)
        => new()
        {
            RequestId = requestId,
            Method = request.Method,
            BodyBytes = body,
            HeaderValues = request.Headers
                .Where(header => !HopByHopHeaders.Contains(header.Key))
                .ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
            Destination = staticSite ? TunnelDestinations.StaticSite : TunnelDestinations.RuntimeGateway,
            RuntimeAppId = appId,
            RuntimeDeploymentTargetId = deploymentTargetId,
            RuntimePath = path.TrimStart('/') + request.QueryString
        };

    internal static async Task<(byte[]? Body, bool TooLarge)> ReadBodyAsync(
        HttpRequest request,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength == 0)
        {
            return (null, false);
        }

        await using var body = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var remaining = maxBytes + 1;
        while (remaining > 0)
        {
            var read = await request.Body.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }

        return body.Length > maxBytes
            ? (null, true)
            : (body.ToArray(), false);
    }

    private static TunnelResponse Failure(int statusCode, string error) => new()
    {
        StatusCode = statusCode,
        Error = error,
        BodyBytes = System.Text.Encoding.UTF8.GetBytes(error),
        HeaderValues = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = ["text/plain; charset=utf-8"]
        }
    };
}
