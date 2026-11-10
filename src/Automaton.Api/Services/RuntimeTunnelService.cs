using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Enums;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public sealed record ResolvedTunnelTarget(Runner? Runner, bool StaticSite, TunnelResponse? Failure);

/// <summary>
/// Proxies browser traffic to a self-hosted Runner through its existing SignalR connection.
/// The runner only accepts the fixed loopback runtime gateway destination.
/// </summary>
public sealed class RuntimeTunnelService(
    ApplicationDbContext dbContext,
    RuntimeTunnelRequestSender requestSender,
    IOptions<PublishedAppsConfig> publishedAppsOptions,
    ApiInstanceIdentityService apiInstanceIdentityService,
    ILogger<RuntimeTunnelService> logger)
{
    private readonly PublishedAppsConfig publishedAppsConfig = publishedAppsOptions.Value;

    public async Task<TunnelResponse> ForwardAsync(
        Guid appId,
        Guid deploymentTargetId,
        HttpRequest request,
        string path,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(appId, deploymentTargetId, request, path, cancellationToken);
        return resolved.Failure ?? await requestSender.ForwardAsync(
            appId, deploymentTargetId, resolved.Runner!, request, path, cancellationToken, resolved.StaticSite);
    }

    /// <summary>Runs every check a tunnel request needs and returns the Runner to use, or the response to send instead.</summary>
    public async Task<ResolvedTunnelTarget> ResolveAsync(
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
                target.WebApp.DeletionClaimId == null &&
                target.GenericRuntimeDeployed,
                cancellationToken);

        if (target?.ExecutionRunner is not { } runner)
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, request.Host.Host, path, "target-not-found");
            return new ResolvedTunnelTarget(null, false, Failure(StatusCodes.Status404NotFound, "The deployed app target was not found."));
        }

        if (!IsExpectedAppHost(request.Host.Host, target, publishedAppsConfig))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "unexpected-host");
            return new ResolvedTunnelTarget(null, false, Failure(StatusCodes.Status404NotFound, "The deployed app target was not found."));
        }

        if (target.WebApp.IsPaused)
            return new ResolvedTunnelTarget(null, false, PausedPage());

        var isHostedRunner = await dbContext.HostedRunners.AnyAsync(hostedRunner =>
            hostedRunner.RunnerId == runner.Id,
            cancellationToken);
        if (isHostedRunner)
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "hosted-runner");
            return new ResolvedTunnelTarget(null, false, Failure(StatusCodes.Status404NotFound, "Per-user Hosted runtime is retired. Assign a Management Runner."));
        }

        if (!SelfHostedRuntimeTunnelPolicy.AllowsRuntimeTunnel(runner.Purpose, runner.ApplicationProfile))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "unsupported-profile");
            return new ResolvedTunnelTarget(null, false, Failure(StatusCodes.Status404NotFound, "The deployed app target was not found."));
        }

        if (!IsCurrentApiInstance(runner.ApiInstanceId, apiInstanceIdentityService.ApiInstanceId))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason} RunnerApiInstanceId={RunnerApiInstanceId} CurrentApiInstanceId={CurrentApiInstanceId}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "runner-wrong-api-instance",
                runner.ApiInstanceId, apiInstanceIdentityService.ApiInstanceId);
            return new ResolvedTunnelTarget(null, false, Failure(StatusCodes.Status503ServiceUnavailable, "The assigned Runner is connected to another API instance."));
        }

        if (!RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "runner-offline");
            return new ResolvedTunnelTarget(null, false, Failure(StatusCodes.Status503ServiceUnavailable, "The assigned Runner is not connected."));
        }

        return new ResolvedTunnelTarget(runner, UsesMachineStaticSite(target, runner, request.Host.Host), null);
    }

    private const string PausedHtml =
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        "<title>This app is paused</title><style>body{font-family:system-ui,sans-serif;display:grid;place-items:center;min-height:100vh;margin:0;color:#333}" +
        "main{text-align:center;padding:1rem}h1{font-size:1.5rem}p{color:#666}</style></head>" +
        "<body><main><h1>This app is paused</h1><p>Its owner has paused it. Please come back later.</p></main></body></html>";

    /// <summary>The short page a paused app's address answers with (503); nothing of the app is reached.</summary>
    internal static TunnelResponse PausedPage() => new()
    {
        StatusCode = StatusCodes.Status503ServiceUnavailable,
        Error = "This app is paused.",
        BodyBytes = System.Text.Encoding.UTF8.GetBytes(PausedHtml),
        HeaderValues = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = ["text/html; charset=utf-8"],
            ["Cache-Control"] = ["no-store"],
            ["Retry-After"] = ["3600"]
        }
    };

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

    internal static Task<(byte[]? Body, bool TooLarge)> ReadBodyAsync(
        HttpRequest request,
        int maxBytes,
        CancellationToken cancellationToken)
        => RuntimeTunnelRequestSender.ReadBodyAsync(request, maxBytes, cancellationToken);

    internal static bool IsExpectedAppHost(string host, AppDeploymentTarget target, PublishedAppsConfig publishedAppsConfig)
    {
        var privateHost = PublishedAppHostNames.BuildPrivateAppHost(
            target.WebAppId, publishedAppsConfig.PublicBaseUrlHost);
        if (string.Equals(host, privateHost, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!target.IsDefault ||
            !target.WebApp.IsPublished ||
            target.WebApp.RuntimeVisibility != AppRuntimeVisibilities.Public ||
            target.WebApp.AccessStatus != AppAccessStatuses.Active ||
            string.IsNullOrWhiteSpace(target.WebApp.PublicUrl))
            return false;

        var publicHost = PublishedAppHostNames.BuildPublishedAppHost(
            target.WebApp.PublicUrl, publishedAppsConfig.PublicBaseUrlHost);
        return string.Equals(host, publicHost, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsCurrentApiInstance(string? runnerApiInstanceId, string currentApiInstanceId)
        => !string.IsNullOrWhiteSpace(runnerApiInstanceId) &&
            string.Equals(runnerApiInstanceId, currentApiInstanceId, StringComparison.Ordinal);

    private bool UsesMachineStaticSite(AppDeploymentTarget target, Runner runner, string host)
        => target.IsDefault
            && ApplicationContractSerialization.Deserialize(target.WebApp.ApplicationContractJson).IsStaticSite
            && RunnerApplicationProfiles.Normalize(runner.ApplicationProfile) == RunnerApplicationProfiles.MachineManagement
            && (string.Equals(host,
                    PublishedAppHostNames.BuildPrivateAppHost(target.WebAppId, publishedAppsConfig.PublicBaseUrlHost),
                    StringComparison.OrdinalIgnoreCase)
                || (target.WebApp.IsPublished
                    && target.WebApp.RuntimeVisibility == AppRuntimeVisibilities.Public
                    && !string.IsNullOrWhiteSpace(target.WebApp.PublicUrl)
                    && string.Equals(host,
                        PublishedAppHostNames.BuildPublishedAppHost(target.WebApp.PublicUrl, publishedAppsConfig.PublicBaseUrlHost),
                        StringComparison.OrdinalIgnoreCase)));
}
