using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

/// <summary>
/// Proxies browser traffic to a self-hosted Runner through its existing SignalR connection.
/// The runner only accepts the fixed loopback runtime gateway destination.
/// </summary>
public sealed class RuntimeTunnelService(
    ApplicationDbContext dbContext,
    IHubContext<AutomatonHub> hub,
    TunnelRequestRegistry registry,
    IOptions<RuntimeTunnelConfig> options,
    IOptions<PublishedAppsConfig> publishedAppsOptions,
    ILogger<RuntimeTunnelService> logger)
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade", "Host", "Content-Length"
    };

    private readonly RuntimeTunnelConfig config = options.Value;
    private readonly PublishedAppsConfig publishedAppsConfig = publishedAppsOptions.Value;

    public async Task<TunnelResponse> ForwardAsync(
        Guid appId,
        Guid deploymentTargetId,
        HttpRequest request,
        string path,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.AppDeploymentTargets
            .Include(target => target.ExecutionRunner)
            .Include(target => target.WebApp)
            .SingleOrDefaultAsync(target =>
                target.Id == deploymentTargetId &&
                target.WebAppId == appId &&
                target.ExecutionRunnerId != null &&
                (target.FrontendDeployed || target.BackendDeployed),
                cancellationToken);

        if (target?.ExecutionRunner is not { } runner)
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, request.Host.Host, path, "target-not-found");
            return Failure(StatusCodes.Status404NotFound, "The deployed app target was not found.");
        }

        if (!IsExpectedAppHost(request.Host.Host, target, publishedAppsConfig))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "unexpected-host");
            return Failure(StatusCodes.Status404NotFound, "The deployed app target was not found.");
        }

        var isHostedRunner = await dbContext.HostedRunners.AnyAsync(hostedRunner =>
            hostedRunner.DeletedAt == null && hostedRunner.RunnerId == runner.Id,
            cancellationToken);
        if (isHostedRunner)
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "hosted-runner");
            return Failure(StatusCodes.Status404NotFound, "This app target uses the hosted runtime route.");
        }

        if (!SelfHostedRuntimeTunnelPolicy.AllowsRuntimeTunnel(runner.Purpose, runner.ApplicationProfile))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "unsupported-profile");
            return Failure(StatusCodes.Status404NotFound, "The deployed app target was not found.");
        }

        if (!RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "runner-offline");
            return Failure(StatusCodes.Status503ServiceUnavailable, "The assigned Runner is not connected.");
        }

        if (request.ContentLength is > 0 && request.ContentLength > config.MaxBodyBytes)
            return Failure(StatusCodes.Status413PayloadTooLarge, "The request body exceeds the runtime tunnel limit.");

        var (body, bodyTooLarge) = await ReadBodyAsync(request, config.MaxBodyBytes, cancellationToken);
        if (bodyTooLarge)
            return Failure(StatusCodes.Status413PayloadTooLarge, "The request body exceeds the runtime tunnel limit.");

        var (requestId, responseTask) = registry.Register(
            TimeSpan.FromSeconds(config.TimeoutSeconds), cancellationToken);
        var tunnelRequest = new TunnelRequest
        {
            RequestId = requestId,
            Method = request.Method,
            BodyBytes = body,
            HeaderValues = request.Headers
                .Where(header => !HopByHopHeaders.Contains(header.Key))
                .ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
            Destination = TunnelDestinations.RuntimeGateway,
            RuntimeAppId = appId,
            RuntimeDeploymentTargetId = deploymentTargetId,
            RuntimePath = path.TrimStart('/') + request.QueryString
        };

        try
        {
            await hub.Clients.Client(runner.ConnectionId!)
                .SendAsync(AutomatonHubMethods.TunnelRequest, tunnelRequest, cancellationToken);
            var response = await responseTask;
            if (response.StatusCode >= StatusCodes.Status400BadRequest)
            {
                logger.LogWarning(
                    "Runner tunnel response: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} StatusCode={StatusCode}",
                    appId, deploymentTargetId, runner.Id, request.Host.Host, path, response.StatusCode);
            }

            return response;
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Runtime tunnel timed out for app {AppId}, target {TargetId}, runner {RunnerId}",
                appId, deploymentTargetId, runner.Id);
            return Failure(StatusCodes.Status504GatewayTimeout, "The Runner did not respond in time.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Runtime tunnel failed for app {AppId}, target {TargetId}, runner {RunnerId}",
                appId, deploymentTargetId, runner.Id);
            return Failure(StatusCodes.Status502BadGateway, "The Runner runtime tunnel is unavailable.");
        }
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

    internal static async Task<(byte[]? Body, bool TooLarge)> ReadBodyAsync(
        HttpRequest request,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength == 0)
            return (null, false);

        await using var body = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var remaining = maxBytes + 1;
        while (remaining > 0)
        {
            var read = await request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
                break;
            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }

        return body.Length > maxBytes
            ? (null, true)
            : (body.ToArray(), false);
    }

    internal static bool IsExpectedAppHost(string host, AppDeploymentTarget target, PublishedAppsConfig publishedAppsConfig)
    {
        var privateHost = PublishedAppHostNames.BuildPrivateAppHost(
            target.WebAppId, publishedAppsConfig.PublicBaseUrlHost);
        if (string.Equals(host, privateHost, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!target.IsDefault ||
            target.WebApp.RuntimeVisibility != AppRuntimeVisibilities.Public ||
            target.WebApp.AccessStatus != AppAccessStatuses.Active ||
            string.IsNullOrWhiteSpace(target.WebApp.PublicUrl))
            return false;

        var publicHost = PublishedAppHostNames.BuildPublishedAppHost(
            target.WebApp.PublicUrl, publishedAppsConfig.PublicBaseUrlHost);
        return string.Equals(host, publicHost, StringComparison.OrdinalIgnoreCase);
    }
}
