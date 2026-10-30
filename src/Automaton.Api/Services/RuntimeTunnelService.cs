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
            hostedRunner.RunnerId == runner.Id,
            cancellationToken);
        if (isHostedRunner)
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "hosted-runner");
            return Failure(StatusCodes.Status404NotFound, "Per-user Hosted runtime is retired. Assign a Management Runner.");
        }

        if (!SelfHostedRuntimeTunnelPolicy.AllowsRuntimeTunnel(runner.Purpose, runner.ApplicationProfile))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "unsupported-profile");
            return Failure(StatusCodes.Status404NotFound, "The deployed app target was not found.");
        }

        if (!IsCurrentApiInstance(runner.ApiInstanceId, apiInstanceIdentityService.ApiInstanceId))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason} RunnerApiInstanceId={RunnerApiInstanceId} CurrentApiInstanceId={CurrentApiInstanceId}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "runner-wrong-api-instance",
                runner.ApiInstanceId, apiInstanceIdentityService.ApiInstanceId);
            return Failure(StatusCodes.Status503ServiceUnavailable, "The assigned Runner is connected to another API instance.");
        }

        if (!RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, DateTime.UtcNow))
        {
            logger.LogWarning(
                "Runtime tunnel request rejected: AppId={AppId} TargetId={TargetId} RunnerId={RunnerId} Host={Host} RuntimePath={RuntimePath} RejectedReason={RejectedReason}",
                appId, deploymentTargetId, runner.Id, request.Host.Host, path, "runner-offline");
            return Failure(StatusCodes.Status503ServiceUnavailable, "The assigned Runner is not connected.");
        }

        var staticSite = UsesMachineStaticSite(target, runner, request.Host.Host);
        return await requestSender.ForwardAsync(
            appId, deploymentTargetId, runner, request, path, cancellationToken, staticSite);
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
            && target.WebApp.IsPublished
            && target.WebApp.RuntimeVisibility == AppRuntimeVisibilities.Public
            && ApplicationContractSerialization.Deserialize(target.WebApp.ApplicationContractJson).IsStaticSite
            && !string.IsNullOrWhiteSpace(target.WebApp.PublicUrl)
            && RunnerApplicationProfiles.Normalize(runner.ApplicationProfile) == RunnerApplicationProfiles.MachineManagement
            && string.Equals(host,
                PublishedAppHostNames.BuildPublishedAppHost(target.WebApp.PublicUrl, publishedAppsConfig.PublicBaseUrlHost),
                StringComparison.OrdinalIgnoreCase);
}
