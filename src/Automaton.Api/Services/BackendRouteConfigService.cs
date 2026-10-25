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
        string RuntimeServiceName,
        string? Slug,
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
        var eligibleTargets = CreateEligibleTargets(
            pendingGenericRuntimeTargetId,
            pendingGenericRuntimeDeployed);
        var routes = await LoadSelfHostedRoutesAsync(eligibleTargets, cancellationToken);

        await WriteRoutesAsync(routes, cancellationToken);
    }

    private IQueryable<Automaton.Api.Entities.AppDeploymentTarget> CreateEligibleTargets(
        Guid? pendingGenericRuntimeTargetId,
        bool? pendingGenericRuntimeDeployed)
    {
        if (pendingGenericRuntimeTargetId.HasValue != pendingGenericRuntimeDeployed.HasValue)
        {
            throw new ArgumentException(
                "A pending generic runtime target and its deployment state must be supplied together.",
                nameof(pendingGenericRuntimeTargetId));
        }

        var targets = dbContext.AppDeploymentTargets.AsNoTracking();
        if (pendingGenericRuntimeTargetId is not { } targetId)
        {
            return targets.Where(target => target.GenericRuntimeDeployed);
        }

        var deployed = pendingGenericRuntimeDeployed!.Value;
        return targets.Where(target => target.Id == targetId
            ? deployed
            : target.GenericRuntimeDeployed);
    }

    private async Task<List<RouteTarget>> LoadSelfHostedRoutesAsync(
        IQueryable<Automaton.Api.Entities.AppDeploymentTarget> eligibleTargets,
        CancellationToken cancellationToken)
    {
        var candidates = await eligibleTargets
            .Where(target =>
                target.ExecutionRunnerId != null &&
                !dbContext.HostedRunners.Any(hostedRunner =>
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
                    candidate.Target.WebApp.IsPublished &&
                    candidate.Target.WebApp.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                    candidate.Target.WebApp.AccessStatus == AppAccessStatuses.Active
                    ? candidate.Target.WebApp.PublicUrl
                    : null
            })
            .ToListAsync(cancellationToken);

        return candidates
            .Where(candidate => SelfHostedRuntimeTunnelPolicy.AllowsRuntimeTunnel(
                candidate.Purpose,
                candidate.ApplicationProfile))
            .Select(candidate => new RouteTarget(
                candidate.WebAppId,
                candidate.Id,
                candidate.IsDefault,
                publishedAppsConfig.RuntimeTunnelServiceName.Trim(),
                candidate.Slug,
                candidate.RequiresAuthentication))
            .ToList();
    }

    private async Task WriteRoutesAsync(List<RouteTarget> routes, CancellationToken cancellationToken)
    {
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

        await WriteConfigAtomicallyAsync(routes, targetPath, tempPath, backupPath, cancellationToken);

        logger.LogInformation(
            "Refreshed managed runtime routes for {RouteCount} target(s) at {TargetPath}",
            routes.Count,
            targetPath);
    }

    private async Task WriteConfigAtomicallyAsync(
        IReadOnlyCollection<RouteTarget> routes,
        string targetPath,
        string tempPath,
        string backupPath,
        CancellationToken cancellationToken)
    {
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
    }

    private string BuildConfig(IReadOnlyCollection<RouteTarget> routes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("http:");

        AppendRouters(builder, routes);
        AppendMiddlewares(builder, routes);

        return builder.ToString();
    }

    private void AppendRouters(StringBuilder builder, IReadOnlyCollection<RouteTarget> routes)
    {
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

    }

    private void AppendMiddlewares(StringBuilder builder, IReadOnlyCollection<RouteTarget> routes)
    {
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

    }

    private void AppendRuntimeRouter(
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
        builder.AppendLine(string.IsNullOrWhiteSpace(publishedAppsConfig.CertificateResolver)
            ? "      tls: {}"
            : $"      tls: {{ certResolver: {System.Text.Json.JsonSerializer.Serialize(publishedAppsConfig.CertificateResolver)} }}");
        builder.AppendLine($"      rule: \"Host(`{host}`) && PathPrefix(`{pathPrefix}`)\"");
        builder.AppendLine($"      priority: {priority}");
        builder.AppendLine($"      service: \"{route.RuntimeServiceName}\"");
        var middlewares = route.RequiresAuthentication
            ? $"[\"{key}-auth-headers\", \"{key}-auth\", \"{key}-auth-strip\", \"{key}-rewrite\"]"
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
        builder.AppendLine($"    {key}-auth-strip:");
        builder.AppendLine("      headers:");
        builder.AppendLine("        customRequestHeaders:");
        builder.AppendLine("          X-App-Id: \"\"");
        builder.AppendLine("          X-Automaton-Runtime-Auth: \"\"");
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

    private static string BuildTargetRoutePrefix(Guid appId, Guid deploymentTargetId)
        => PublishedAppHostNames.BuildPrivateTargetPath(appId, deploymentTargetId, false).TrimEnd('/');

    private static string BuildRuntimePath(RouteTarget route, string path)
        => $"/runtime-tunnel/apps/{route.AppId}/targets/{route.DeploymentTargetId}/{path}";

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
