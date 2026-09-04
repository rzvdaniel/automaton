using System.Text;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public class BackendRouteConfigService
{
    private const string RouteFileName = "managed-runtime-routes.yml";

    private readonly ApplicationDbContext dbContext;
    private readonly PublishedAppsConfig publishedAppsConfig;
    private readonly IWebHostEnvironment environment;
    private readonly ILogger<BackendRouteConfigService> logger;

    private sealed record RouteTarget(
        Guid AppId,
        Guid DeploymentTargetId,
        bool IsDefault,
        string RuntimeEndpoint,
        string? Slug,
        bool UsesRuntimeTunnel,
        bool RequiresAuthentication);

    public BackendRouteConfigService(
        ApplicationDbContext dbContext,
        IOptions<PublishedAppsConfig> publishedAppsConfig,
        IWebHostEnvironment environment,
        ILogger<BackendRouteConfigService> logger)
    {
        this.dbContext = dbContext;
        this.publishedAppsConfig = publishedAppsConfig.Value;
        this.environment = environment;
        this.logger = logger;
    }

    /// <summary>
    /// Reconciles managed runtime routes from target-local promotion state.
    /// A pending state is accepted only while a validated generic package
    /// deployment is completing its API-side finalization; it is not persisted
    /// or inferred from the mutable application contract.
    /// </summary>
    public virtual async Task RefreshAsync(
        CancellationToken cancellationToken = default,
        Guid? pendingGenericRuntimeTargetId = null,
        bool? pendingGenericRuntimeDeployed = null)
    {
        if (pendingGenericRuntimeTargetId.HasValue != pendingGenericRuntimeDeployed.HasValue)
        {
            throw new ArgumentException(
                "A pending generic runtime target and its deployment state must be supplied together.",
                nameof(pendingGenericRuntimeTargetId));
        }

        var eligibleTargets = dbContext.AppDeploymentTargets.AsNoTracking();
        if (pendingGenericRuntimeTargetId is { } pendingTargetId)
        {
            var pendingDeploymentState = pendingGenericRuntimeDeployed!.Value;
            eligibleTargets = eligibleTargets.Where(target =>
                target.Id == pendingTargetId
                    ? pendingDeploymentState
                    : target.GenericRuntimeDeployed);
        }
        else
        {
            eligibleTargets = eligibleTargets.Where(target => target.GenericRuntimeDeployed);
        }

        var hostedRoutes = await eligibleTargets
            .Where(target =>
                target.ExecutionRunnerId != null)
            .Join(
                dbContext.HostedRunners.AsNoTracking().Where(r =>
                    r.DeletedAt == null &&
                    r.RunnerId != null &&
                    r.RuntimeEndpoint != null &&
                    r.RuntimeEndpoint != string.Empty),
                target => target.ExecutionRunnerId,
                runner => runner.RunnerId,
                (target, runner) => new
                {
                    AppId = target.WebAppId,
                    DeploymentTargetId = target.Id,
                    target.IsDefault,
                    RuntimeEndpoint = runner.RuntimeEndpoint!,
                    RequiresAuthentication = target.WebApp.RequiresAuthentication,
                    Slug = target.IsDefault &&
                        target.WebApp.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                        target.WebApp.AccessStatus == AppAccessStatuses.Active
                        ? target.WebApp.PublicUrl
                        : null
                })
            .OrderBy(x => x.AppId)
            .ThenBy(x => x.DeploymentTargetId)
            .Select(x => new RouteTarget(
                x.AppId,
                x.DeploymentTargetId,
                x.IsDefault,
                x.RuntimeEndpoint.Trim(),
                x.Slug,
                false,
                x.RequiresAuthentication))
            .ToListAsync(cancellationToken);

        var selfHostedRouteCandidates = await eligibleTargets
            .Where(target =>
                target.ExecutionRunnerId != null &&
                !dbContext.HostedRunners.Any(hostedRunner =>
                    hostedRunner.DeletedAt == null &&
                    hostedRunner.RunnerId == target.ExecutionRunnerId))
            .Join(
                dbContext.Runners.AsNoTracking(),
                target => target.ExecutionRunnerId,
                runner => (Guid?)runner.Id,
                (target, runner) => new
                {
                    Target = target,
                    runner.Purpose,
                    runner.ApplicationProfile
                })
            .OrderBy(candidate => candidate.Target.WebAppId)
            .ThenBy(candidate => candidate.Target.Id)
            .Select(candidate => new
            {
                candidate.Purpose,
                candidate.ApplicationProfile,
                candidate.Target.WebAppId,
                candidate.Target.Id,
                candidate.Target.IsDefault,
                RequiresAuthentication = candidate.Target.WebApp.RequiresAuthentication,
                Slug = candidate.Target.IsDefault &&
                    candidate.Target.WebApp.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                    candidate.Target.WebApp.AccessStatus == AppAccessStatuses.Active
                    ? candidate.Target.WebApp.PublicUrl
                    : null
            })
            .ToListAsync(cancellationToken);

        var selfHostedRoutes = selfHostedRouteCandidates
            .Where(candidate => SelfHostedRuntimeTunnelPolicy.AllowsRuntimeTunnel(
                candidate.Purpose,
                candidate.ApplicationProfile))
            .Select(candidate => new RouteTarget(
                candidate.WebAppId,
                candidate.Id,
                candidate.IsDefault,
                publishedAppsConfig.RuntimeTunnelServiceName.Trim(),
                candidate.Slug,
                true,
                candidate.RequiresAuthentication))
            .ToList();

        var routes = hostedRoutes.Concat(selfHostedRoutes).ToList();

        var rootPath = ResolveRootPath();
        Directory.CreateDirectory(rootPath);

        var targetPath = Path.Combine(rootPath, RouteFileName);
        var tempPath = Path.Combine(rootPath, $"{RouteFileName}.tmp");
        var backupPath = Path.Combine(rootPath, $"{RouteFileName}.bak");

        if (routes.Count == 0)
        {
            if (File.Exists(targetPath))
            {
                File.Copy(targetPath, backupPath, overwrite: true);
                TryDelete(targetPath);
            }

            logger.LogInformation(
                "Removed managed runtime routes because no active routes remain at {TargetPath}",
                targetPath);
            return;
        }

        var content = BuildConfig(routes);
        await File.WriteAllTextAsync(tempPath, content, new UTF8Encoding(false), cancellationToken);

        try
        {
            if (File.Exists(targetPath))
            {
                File.Copy(targetPath, backupPath, overwrite: true);
            }

            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        logger.LogInformation(
            "Refreshed managed runtime routes for {RouteCount} target(s) at {TargetPath}",
            routes.Count,
            targetPath);
    }

    private string BuildConfig(IReadOnlyCollection<RouteTarget> routes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("http:");

        builder.AppendLine("  routers:");
        foreach (var route in routes.Where(route => route.IsDefault))
        {
            var key = $"private-{route.AppId:N}";
            var host = PublishedAppHostNames.BuildPrivateAppHost(
                route.AppId, publishedAppsConfig.PublicBaseUrlHost);
            AppendRuntimeRouter(builder, route, key, host,
                PublishedAppHostNames.BuildPrivateTargetPath(route.AppId, route.DeploymentTargetId, true), 50);
        }

        foreach (var route in routes.Where(route => !route.IsDefault))
        {
            var key = $"target-{route.DeploymentTargetId:N}";
            var prefix = BuildTargetRoutePrefix(route.AppId, route.DeploymentTargetId);
            var host = PublishedAppHostNames.BuildPrivateAppHost(
                route.AppId, publishedAppsConfig.PublicBaseUrlHost);
            AppendRuntimeRouter(builder, route, key, host, prefix, 110);
        }

        foreach (var route in routes.Where(route => !string.IsNullOrWhiteSpace(route.Slug)))
        {
            var key = $"slug-{route.AppId:N}";
            var host = PublishedAppHostNames.BuildPublishedAppHost(
                route.Slug!, publishedAppsConfig.PublicBaseUrlHost);
            AppendRuntimeRouter(builder, route, key, host, "/", 50);
        }

        builder.AppendLine("  middlewares:");
        foreach (var route in routes.Where(route => route.RequiresAuthentication))
        {
            var key = route.IsDefault
                ? $"private-{route.AppId:N}"
                : $"target-{route.DeploymentTargetId:N}";
            AppendRuntimeAuthorization(builder, route, key);

            if (!string.IsNullOrWhiteSpace(route.Slug))
            {
                AppendRuntimeAuthorization(builder, route, $"slug-{route.AppId:N}");
            }
        }

        foreach (var route in routes.Where(route => route.IsDefault))
        {
            var key = $"private-{route.AppId:N}";
            AppendRuntimeRewrite(builder, route, key, "^/(.*)$");
        }

        foreach (var route in routes.Where(route => !route.IsDefault))
        {
            var key = $"target-{route.DeploymentTargetId:N}";
            var prefix = BuildTargetRoutePrefix(route.AppId, route.DeploymentTargetId);
            AppendRuntimeRewrite(builder, route, key, $"^{prefix}(?:/(.*))?$");
        }

        foreach (var route in routes.Where(route => !string.IsNullOrWhiteSpace(route.Slug)))
        {
            var key = $"slug-{route.AppId:N}";
            AppendRuntimeRewrite(builder, route, key, "^/(.*)$");
        }

        builder.AppendLine("  services:");
        foreach (var route in routes.Where(route => !route.UsesRuntimeTunnel && !string.IsNullOrWhiteSpace(route.Slug)))
        {
            var key = $"slug-{route.AppId:N}";
            AppendRuntimeService(builder, route, key);
        }

        foreach (var route in routes.Where(route => !route.UsesRuntimeTunnel))
        {
            var key = route.IsDefault
                ? $"private-{route.AppId:N}"
                : $"target-{route.DeploymentTargetId:N}";
            AppendRuntimeService(builder, route, key);
        }

        return builder.ToString();
    }

    private static void AppendRuntimeRouter(
        StringBuilder builder,
        RouteTarget route,
        string key,
        string host,
        string pathPrefix,
        int priority)
    {
        builder.AppendLine($"    {key}-runtime:");
        builder.AppendLine("      entryPoints:");
        builder.AppendLine("        - websecure");
        builder.AppendLine("      tls: {}");
        builder.AppendLine($"      rule: \"Host(`{host}`) && PathPrefix(`{pathPrefix}`)\"");
        builder.AppendLine($"      priority: {priority}");
        builder.AppendLine($"      service: \"{GetServiceName(route, key)}\"");
        var middlewares = route.RequiresAuthentication
            ? $"[\"{key}-auth-headers\", \"{key}-auth\", \"{key}-rewrite\"]"
            : $"[\"{key}-rewrite\"]";
        builder.AppendLine($"      middlewares: {middlewares}");
    }

    private void AppendRuntimeAuthorization(StringBuilder builder, RouteTarget route, string key)
    {
        builder.AppendLine($"    {key}-auth-headers:");
        builder.AppendLine("      headers:");
        builder.AppendLine("        customRequestHeaders:");
        builder.AppendLine($"          X-App-Id: \"{route.AppId}\"");
        builder.AppendLine($"          X-Automaton-Runtime-Auth: \"{publishedAppsConfig.RuntimeProxyAuthKey}\"");
        builder.AppendLine($"    {key}-auth:");
        builder.AppendLine("      forwardAuth:");
        builder.AppendLine($"        address: \"{publishedAppsConfig.ApiBaseUrl.TrimEnd('/')}/internal/runtime/auth\"");
        builder.AppendLine("        trustForwardHeader: true");
        builder.AppendLine("        authRequestHeaders:");
        builder.AppendLine("          - Cookie");
        builder.AppendLine("          - X-App-Id");
        builder.AppendLine("          - X-Automaton-Runtime-Auth");
    }

    private static void AppendRuntimeRewrite(
        StringBuilder builder,
        RouteTarget route,
        string key,
        string regex)
    {
        builder.AppendLine($"    {key}-rewrite:");
        builder.AppendLine("      replacePathRegex:");
        builder.AppendLine($"        regex: \"{regex}\"");
        builder.AppendLine($"        replacement: \"{BuildRuntimePath(route, "$1")}\"");
    }

    private static void AppendRuntimeService(StringBuilder builder, RouteTarget route, string key)
    {
        builder.AppendLine($"    {key}:");
        builder.AppendLine("      loadBalancer:");
        builder.AppendLine("        servers:");
        builder.AppendLine($"          - url: \"{route.RuntimeEndpoint}\"");
    }

    private static string BuildTargetRoutePrefix(Guid appId, Guid deploymentTargetId)
        => PublishedAppHostNames.BuildPrivateTargetPath(appId, deploymentTargetId, false).TrimEnd('/');

    private static string BuildRuntimePath(RouteTarget route, string path)
    {
        var gatewayPath = $"/apps/{route.AppId}/targets/{route.DeploymentTargetId}/{path}";
        return route.UsesRuntimeTunnel
            ? $"/runtime-tunnel{gatewayPath}"
            : gatewayPath;
    }

    private static string GetServiceName(RouteTarget route, string routeServiceName)
        => route.UsesRuntimeTunnel ? route.RuntimeEndpoint : routeServiceName;

    private string ResolveRootPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(publishedAppsConfig.TraefikDynamicConfigRoot)
            ? "AppData/traefik-dynamic"
            : publishedAppsConfig.TraefikDynamicConfigRoot;

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Keep the existing route file intact even if temp cleanup fails.
        }
    }
}
