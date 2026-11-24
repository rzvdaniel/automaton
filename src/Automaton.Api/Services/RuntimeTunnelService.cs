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

public sealed record ResolvedTunnelTarget(Runner? Runner, bool StaticSite, TunnelFailure? Failure);

/// <summary>
/// Decides whether a browser request for an app may be sent through its Runner's tunnel, and probes an app's pages through it.
/// The Runner only accepts the fixed local destinations (runtime gateway, static site, FastAPI).
/// </summary>
public sealed class RuntimeTunnelService(
    ApplicationDbContext dbContext,
    RunnerTunnelRegistry tunnels,
    IOptions<PublishedAppsConfig> publishedAppsOptions,
    ApiInstanceIdentityService apiInstanceIdentityService,
    ILogger<RuntimeTunnelService> logger,
    RuntimeTunnelTargetCache targetCache) : ITunnelProber
{
    private readonly PublishedAppsConfig publishedAppsConfig = publishedAppsOptions.Value;

    /// <summary>Asks the app for one page through the tunnel and returns its status and headers; the body is not read.</summary>
    public async Task<TunnelProbeResult> ProbeAsync(
        Guid appId, Guid deploymentTargetId, string host, string path, CancellationToken cancellationToken)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(host);
        var resolved = await ResolveAsync(appId, deploymentTargetId, context.Request, path, cancellationToken);
        if (resolved.Failure is { } failure)
            return new TunnelProbeResult(failure.StatusCode, new Dictionary<string, string[]>());
        if (tunnels.TryAcquire(resolved.Runner!.Id) is not { } connection)
            return new TunnelProbeResult(StatusCodes.Status503ServiceUnavailable, new Dictionary<string, string[]>());

        using var lease = connection;
        using var message = RuntimeTunnelRequestSender.CreateRequest(
            HttpMethod.Get, appId, deploymentTargetId, path, string.Empty, context.Request, resolved.StaticSite);
        using var response = await connection.Invoker.SendAsync(message, cancellationToken);
        var headers = response.Headers.Concat(response.Content.Headers)
            .ToDictionary(header => header.Key, header => header.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        return new TunnelProbeResult((int)response.StatusCode, headers);
    }

    /// <summary>Runs every check a tunnel request needs and returns the Runner to use, or the response to send instead.</summary>
    public async Task<ResolvedTunnelTarget> ResolveAsync(
        Guid appId,
        Guid deploymentTargetId,
        HttpRequest request,
        string path,
        CancellationToken cancellationToken)
    {
        var lookup = await targetCache.GetOrLoadAsync(
            appId, deploymentTargetId, () => LoadAsync(appId, deploymentTargetId));
        var target = lookup.Target;

        if (target?.ExecutionRunner is not { } runner)
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, request.Host.Host, path, "target-not-found");
            return new ResolvedTunnelTarget(null, false, TunnelFailure.Text(StatusCodes.Status404NotFound, "The deployed app target was not found."));
        }

        if (!IsExpectedAppHost(request.Host.Host, target, publishedAppsConfig, lookup.VerifiedDomains))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "unexpected-host");
            return new ResolvedTunnelTarget(null, false, TunnelFailure.Text(StatusCodes.Status404NotFound, "The deployed app target was not found."));
        }

        if (target.WebApp.IsPaused)
            return new ResolvedTunnelTarget(null, false, PausedPage());

        if (lookup.IsHostedRunner)
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "hosted-runner");
            return new ResolvedTunnelTarget(null, false, TunnelFailure.Text(StatusCodes.Status404NotFound, "Per-user Hosted runtime is retired. Assign a Management Runner."));
        }

        if (!SelfHostedRuntimeTunnelPolicy.AllowsRuntimeTunnel(runner.Purpose, runner.ApplicationProfile))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "unsupported-profile");
            return new ResolvedTunnelTarget(null, false, TunnelFailure.Text(StatusCodes.Status404NotFound, "The deployed app target was not found."));
        }

        if (!IsServedByThisApi(runner.ApiInstanceId, apiInstanceIdentityService.ApiInstanceId, tunnels.IsConnected(runner.Id)))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason} RunnerApiInstanceId={RunnerApiInstanceId} CurrentApiInstanceId={CurrentApiInstanceId}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "runner-wrong-api-instance",
                runner.ApiInstanceId, apiInstanceIdentityService.ApiInstanceId);
            return new ResolvedTunnelTarget(null, false, TunnelFailure.Text(StatusCodes.Status503ServiceUnavailable, "The assigned Runner is connected to another API instance."));
        }

        if (RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow)
            && !RunnerProtocolCompatibilityPolicy.SupportsAppTunnelOverHttp2(runner.ApiCompatibilityVersion))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason} RunnerProtocol={RunnerProtocol}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "runner-outdated", runner.ApiCompatibilityVersion);
            return new ResolvedTunnelTarget(null, false, TunnelFailure.Text(StatusCodes.Status503ServiceUnavailable, "The assigned Runner must be upgraded to carry app traffic."));
        }

        if (!RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow)
            || !tunnels.IsConnected(runner.Id))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "runner-offline");
            return new ResolvedTunnelTarget(null, false, TunnelFailure.Text(StatusCodes.Status503ServiceUnavailable, "The assigned Runner is not connected."));
        }

        return new ResolvedTunnelTarget(runner, UsesMachineStaticSite(target, runner, request.Host.Host), null);
    }

    private sealed record TargetLookup(AppDeploymentTarget? Target, bool IsHostedRunner, IReadOnlyList<string> VerifiedDomains);

    // Shared by every request waiting on the same lookup, so it must not depend on any one request's cancellation.
    private async Task<TargetLookup> LoadAsync(Guid appId, Guid deploymentTargetId)
    {
        var target = await dbContext.AppDeploymentTargets
            .AsNoTracking()
            .Include(target => target.ExecutionRunner)
            .Include(target => target.WebApp)
            .SingleOrDefaultAsync(target =>
                target.Id == deploymentTargetId &&
                target.WebAppId == appId &&
                target.ExecutionRunnerId != null &&
                target.WebApp.DeletionClaimId == null &&
                target.GenericRuntimeDeployed);
        var isHostedRunner = target?.ExecutionRunner is { } runner &&
            await dbContext.HostedRunners.AnyAsync(hostedRunner => hostedRunner.RunnerId == runner.Id);
        var verifiedDomains = target is null
            ? []
            : await dbContext.AppCustomDomains.AsNoTracking()
                .Where(domain => domain.WebAppId == appId && domain.VerifiedAt != null)
                .Select(domain => domain.Host)
                .ToListAsync();
        return new TargetLookup(target, isHostedRunner, verifiedDomains);
    }

    private const string PausedHtml =
        "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
        "<title>This app is paused</title><style>body{font-family:system-ui,sans-serif;display:grid;place-items:center;min-height:100vh;margin:0;color:#333}" +
        "main{text-align:center;padding:1rem}h1{font-size:1.5rem}p{color:#666}</style></head>" +
        "<body><main><h1>This app is paused</h1><p>Its owner has paused it. Please come back later.</p></main></body></html>";

    /// <summary>The short page a paused app's address answers with (503); nothing of the app is reached.</summary>
    internal static TunnelFailure PausedPage() => new(
        StatusCodes.Status503ServiceUnavailable, PausedHtml, "text/html; charset=utf-8",
        new Dictionary<string, string> { ["Cache-Control"] = "no-store", ["Retry-After"] = "3600" });

    internal static bool IsExpectedAppHost(
        string host,
        AppDeploymentTarget target,
        PublishedAppsConfig publishedAppsConfig,
        IReadOnlyCollection<string>? verifiedDomains = null)
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
        // A verified custom domain (#1178) is routed by Traefik to this public app, so it is an expected host too.
        return string.Equals(host, publicHost, StringComparison.OrdinalIgnoreCase) ||
            (verifiedDomains?.Contains(host, StringComparer.OrdinalIgnoreCase) ?? false);
    }

    // The tunnel registry is in memory, so a Runner with a tunnel here is connected to this API even when its row
    // still names an older instance (everything restarted at once and the row was not refreshed).
    internal static bool IsServedByThisApi(string? runnerApiInstanceId, string currentApiInstanceId, bool hasTunnelHere)
        => hasTunnelHere || IsCurrentApiInstance(runnerApiInstanceId, currentApiInstanceId);

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
