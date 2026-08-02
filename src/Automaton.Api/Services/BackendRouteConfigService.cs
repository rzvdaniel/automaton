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
        string? Slug);

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

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var routes = await dbContext.AppDeploymentTargets
            .AsNoTracking()
            .Where(target =>
                target.ExecutionRunnerId != null &&
                (target.FrontendDeployed || target.BackendDeployed))
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
                x.Slug))
            .ToListAsync(cancellationToken);

        var rootPath = ResolveRootPath();
        Directory.CreateDirectory(rootPath);

        var targetPath = Path.Combine(rootPath, RouteFileName);
        var tempPath = Path.Combine(rootPath, $"{RouteFileName}.tmp");
        var backupPath = Path.Combine(rootPath, $"{RouteFileName}.bak");

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
            "Refreshed managed runtime backend routes for {RouteCount} app(s) at {TargetPath}",
            routes.Count,
            targetPath);
    }

    private string BuildConfig(IReadOnlyCollection<RouteTarget> routes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("http:");

        if (routes.Count == 0)
        {
            builder.AppendLine("  routers: {}");
            builder.AppendLine("  middlewares: {}");
            builder.AppendLine("  services: {}");
            return builder.ToString();
        }

        builder.AppendLine("  routers:");
        foreach (var route in routes.Where(route => route.IsDefault))
        {
            var key = $"private-{route.AppId:N}";
            var host = BuildPrivateAppHost(route.AppId);
            builder.AppendLine($"    {key}-api:");
            builder.AppendLine("      entryPoints:");
            builder.AppendLine("        - websecure");
            builder.AppendLine("      tls: {}");
            builder.AppendLine($"      rule: \"Host(`{host}`) && PathPrefix(`/api`)\"");
            builder.AppendLine("      priority: 100");
            builder.AppendLine($"      service: \"{key}\"");
            builder.AppendLine("      middlewares:");
            builder.AppendLine($"        - \"{key}-api-rewrite\"");

            builder.AppendLine($"    {key}-frontend:");
            builder.AppendLine("      entryPoints:");
            builder.AppendLine("        - websecure");
            builder.AppendLine("      tls: {}");
            builder.AppendLine($"      rule: \"Host(`{host}`) && PathPrefix(`/`)\"");
            builder.AppendLine("      priority: 1");
            builder.AppendLine($"      service: \"{key}\"");
            builder.AppendLine($"      middlewares: [\"{key}-frontend-rewrite\"]");
        }

        foreach (var route in routes.Where(route => !route.IsDefault))
        {
            var key = $"target-{route.DeploymentTargetId:N}";
            var prefix = BuildTargetRoutePrefix(route.AppId, route.DeploymentTargetId);
            var host = BuildPrivateAppHost(route.AppId);
            builder.AppendLine($"    {key}:");
            builder.AppendLine("      entryPoints:");
            builder.AppendLine("        - websecure");
            builder.AppendLine("      tls: {}");
            builder.AppendLine($"      rule: \"Host(`{host}`) && PathPrefix(`{prefix}`)\"");
            builder.AppendLine("      priority: 110");
            builder.AppendLine($"      service: \"{key}\"");
            builder.AppendLine($"      middlewares: [\"{key}-rewrite\"]");
        }

        foreach (var route in routes.Where(route => !string.IsNullOrWhiteSpace(route.Slug)))
        {
            var key = $"slug-{route.AppId:N}";
            var host = BuildPublishedAppHost(route.Slug!);
            builder.AppendLine($"    {key}-api:");
            builder.AppendLine("      entryPoints:");
            builder.AppendLine("        - websecure");
            builder.AppendLine("      tls: {}");
            builder.AppendLine($"      rule: \"Host(`{host}`) && PathPrefix(`/api`)\"");
            builder.AppendLine("      priority: 100");
            builder.AppendLine($"      service: \"{key}\"");
            builder.AppendLine("      middlewares:");
            builder.AppendLine($"        - \"{key}-api-rewrite\"");

            builder.AppendLine($"    {key}-frontend:");
            builder.AppendLine("      entryPoints:");
            builder.AppendLine("        - websecure");
            builder.AppendLine("      tls: {}");
            builder.AppendLine($"      rule: \"Host(`{host}`) && PathPrefix(`/`)\"");
            builder.AppendLine("      priority: 1");
            builder.AppendLine($"      service: \"{key}\"");
            builder.AppendLine($"      middlewares: [\"{key}-frontend-rewrite\"]");
        }

        builder.AppendLine("  middlewares:");
        foreach (var route in routes.Where(route => route.IsDefault))
        {
            var key = $"private-{route.AppId:N}";
            builder.AppendLine($"    {key}-api-rewrite:");
            builder.AppendLine("      replacePathRegex:");
            builder.AppendLine("        regex: \"^/api(?:/(.*))?$\"");
            builder.AppendLine($"        replacement: \"/apps/{route.AppId}/targets/{route.DeploymentTargetId}/api/$1\"");
            builder.AppendLine($"    {key}-frontend-rewrite:");
            builder.AppendLine("      replacePathRegex:");
            builder.AppendLine("        regex: \"^/(.*)$\"");
            builder.AppendLine($"        replacement: \"/apps/{route.AppId}/targets/{route.DeploymentTargetId}/$1\"");
        }

        foreach (var route in routes.Where(route => !route.IsDefault))
        {
            var key = $"target-{route.DeploymentTargetId:N}";
            var prefix = BuildTargetRoutePrefix(route.AppId, route.DeploymentTargetId);
            builder.AppendLine($"    {key}-rewrite:");
            builder.AppendLine("      replacePathRegex:");
            builder.AppendLine($"        regex: \"^{prefix}(?:/(.*))?$\"");
            builder.AppendLine($"        replacement: \"/apps/{route.AppId}/targets/{route.DeploymentTargetId}/$1\"");
        }

        foreach (var route in routes.Where(route => !string.IsNullOrWhiteSpace(route.Slug)))
        {
            var key = $"slug-{route.AppId:N}";
            builder.AppendLine($"    {key}-api-rewrite:");
            builder.AppendLine("      replacePathRegex:");
            builder.AppendLine("        regex: \"^/api(?:/(.*))?$\"");
            builder.AppendLine($"        replacement: \"/apps/{route.AppId}/targets/{route.DeploymentTargetId}/api/$1\"");
            builder.AppendLine($"    {key}-frontend-rewrite:");
            builder.AppendLine("      replacePathRegex:");
            builder.AppendLine("        regex: \"^/(.*)$\"");
            builder.AppendLine($"        replacement: \"/apps/{route.AppId}/targets/{route.DeploymentTargetId}/$1\"");
        }

        builder.AppendLine("  services:");
        foreach (var route in routes.Where(route => !string.IsNullOrWhiteSpace(route.Slug)))
        {
            var key = $"slug-{route.AppId:N}";
            builder.AppendLine($"    {key}:");
            builder.AppendLine("      loadBalancer:");
            builder.AppendLine("        servers:");
            builder.AppendLine($"          - url: \"{route.RuntimeEndpoint}\"");
        }

        foreach (var route in routes)
        {
            var key = route.IsDefault
                ? $"private-{route.AppId:N}"
                : $"target-{route.DeploymentTargetId:N}";
            builder.AppendLine($"    {key}:");
            builder.AppendLine("      loadBalancer:");
            builder.AppendLine("        servers:");
            builder.AppendLine($"          - url: \"{route.RuntimeEndpoint}\"");
        }

        return builder.ToString();
    }

    private static string BuildTargetRoutePrefix(Guid appId, Guid deploymentTargetId)
        => $"/a/{appId}/t/{deploymentTargetId}";

    private string BuildPublishedAppHost(string slug)
    {
        var baseHost = publishedAppsConfig.PublicBaseUrlHost;
        const string appsSuffix = ".apps.";
        var appsMarker = baseHost.IndexOf(appsSuffix, StringComparison.OrdinalIgnoreCase);
        if (appsMarker >= 0)
        {
            return $"{slug}{baseHost[appsMarker..]}";
        }

        return $"{slug}.{baseHost}";
    }

    private string BuildPrivateAppHost(Guid appId)
    {
        var baseHost = publishedAppsConfig.PublicBaseUrlHost;
        const string appsSuffix = ".apps.";
        var appsMarker = baseHost.IndexOf(appsSuffix, StringComparison.OrdinalIgnoreCase);
        var suffix = appsMarker >= 0 ? baseHost[appsMarker..] : $".{baseHost}";
        return $"app-{appId:N}{suffix}";
    }

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
