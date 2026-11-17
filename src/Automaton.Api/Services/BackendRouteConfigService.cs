using System.Text;
using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public class BackendRouteConfigService
{
    private const string RouteFileName = "managed-runtime-routes.yml";

    // The service is scoped, so concurrent deploys each have an instance but share one route file and one temporary
    // file. Refreshes run one at a time, and the later one reads the database after the earlier one has written.
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private readonly ApplicationDbContext dbContext;
    private readonly PublishedAppsConfig publishedAppsConfig;
    private readonly IWebHostEnvironment environment;
    private readonly ILogger<BackendRouteConfigService> logger;
    private readonly RunnerGatewayRegistry? gateways;

    private sealed record RouteTarget(
        Guid AppId,
        Guid DeploymentTargetId,
        bool IsDefault,
        string RuntimeServiceName,
        string? Slug,
        bool RequiresAuthentication,
        IReadOnlyList<string> OpenPaths,
        IReadOnlyList<CustomDomainRoute>? CustomDomains = null,
        Guid? RunnerId = null,
        RunnerGateway? Gateway = null);

    // A verified domain of a public app. The primary one (if any) is the app's address; the others redirect to it.
    private sealed record CustomDomainRoute(Guid Id, string Host, bool IsPrimary);

    public BackendRouteConfigService(
        ApplicationDbContext dbContext,
        IOptions<PublishedAppsConfig> publishedAppsConfig,
        IWebHostEnvironment environment,
        ILogger<BackendRouteConfigService> logger,
        RunnerGatewayRegistry? gateways = null)
    {
        this.gateways = gateways;
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

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            var routes = await LoadSelfHostedRoutesAsync(eligibleTargets, cancellationToken);

            await WriteRoutesAsync(routes, cancellationToken);
        }
        finally
        {
            RefreshLock.Release();
        }
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

        var targets = dbContext.AppDeploymentTargets.AsNoTracking()
            .Where(target => target.WebApp.DeletionClaimId == null);
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
                candidate.Target.ExecutionRunnerId,
                candidate.Target.WebApp.IsPaused,
                RequiresAuthentication = candidate.Target.WebApp.RequiresAuthentication,
                candidate.Target.WebApp.ApplicationContractJson,
                Slug = candidate.Target.IsDefault &&
                    candidate.Target.WebApp.IsPublished &&
                    candidate.Target.WebApp.RuntimeVisibility == AppRuntimeVisibilities.Public &&
                    candidate.Target.WebApp.AccessStatus == AppAccessStatuses.Active
                    ? candidate.Target.WebApp.PublicUrl
                    : null
            })
            .ToListAsync(cancellationToken);

        var domainAppIds = candidates.Where(candidate => candidate.Slug != null).Select(candidate => candidate.WebAppId).Distinct().ToList();
        var domains = domainAppIds.Count == 0
            ? []
            : await dbContext.AppCustomDomains.AsNoTracking()
                .Where(domain => domainAppIds.Contains(domain.WebAppId) && domain.VerifiedAt != null)
                .OrderBy(domain => domain.CreatedAt)
                .Select(domain => new { domain.WebAppId, Route = new CustomDomainRoute(domain.Id, domain.Host, domain.IsPrimary && domain.LiveAt != null) })
                .ToListAsync(cancellationToken);

        return candidates
            .Where(candidate => SelfHostedRuntimeTunnelPolicy.AllowsRuntimeTunnel(
                candidate.Purpose,
                candidate.ApplicationProfile))
            .Select(candidate =>
            {
                var gateway = FindDirectGateway(candidate.ExecutionRunnerId, candidate.IsPaused, candidate.ApplicationContractJson);
                return new RouteTarget(
                    candidate.WebAppId,
                    candidate.Id,
                    candidate.IsDefault,
                    gateway is null
                        ? publishedAppsConfig.RuntimeTunnelServiceName.Trim()
                        : GatewayName(candidate.ExecutionRunnerId!.Value),
                    candidate.Slug,
                    candidate.RequiresAuthentication,
                    ReadOpenPaths(candidate.ApplicationContractJson),
                    candidate.Slug is null
                        ? null
                        : domains.Where(domain => domain.WebAppId == candidate.WebAppId).Select(domain => domain.Route).ToList(),
                    candidate.ExecutionRunnerId,
                    gateway);
            })
            .ToList();
    }

    // A paused app answers with the tunnel's "paused" page and a static site is served by the Runner's static server,
    // so both stay on the tunnel; so does any Runner whose gateway the server could not reach.
    private RunnerGateway? FindDirectGateway(Guid? runnerId, bool isPaused, string contractJson)
    {
        if (isPaused || runnerId is not { } id || gateways?.Find(id) is not { } gateway) return null;
        try { return ApplicationContractSerialization.Deserialize(contractJson).IsStaticSite ? null : gateway; }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    private static string GatewayName(Guid runnerId) => $"gateway-{runnerId:N}";

    private static IReadOnlyList<string> ReadOpenPaths(string contractJson)
    {
        try
        {
            return ApplicationContractSerialization.Deserialize(contractJson).OpenPaths
                .Where(ApplicationContractValidator.IsValidOpenPath)
                .Take(ApplicationContractValidator.MaxOpenPaths)
                .ToList();
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    private static string BuildOpenPathRule(string openPath)
        => openPath.EndsWith("/*", StringComparison.Ordinal)
            ? $"PathPrefix(`{openPath[..^1]}`)"
            : $"Path(`{openPath}`)";

    // Never let a request climb out of an open path with dot segments or encoded slashes.
    private const string OpenPathTraversalGuard = " && !PathRegexp(`([.][.]|%2[eEfF]|%5[cC])`)";

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
                File.Delete(targetPath);
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
        AppendGatewayServices(builder, routes);

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
                PublishedAppHostNames.BuildPrivateTargetPath(route.AppId, route.DeploymentTargetId, true), 50, true);

            // Paths the app declares open (webhooks, callbacks) skip sign-in; they sit above the private router.
            for (var index = 0; index < route.OpenPaths.Count; index++)
            {
                AppendRuntimeRouter(builder, route, $"{key}-open{index}", host, string.Empty, 60, false,
                    BuildOpenPathRule(route.OpenPaths[index]) + OpenPathTraversalGuard, $"{key}-rewrite");
            }
        }

        foreach (var route in routes.Where(route => !route.IsDefault))
        {
            var key = $"target-{route.DeploymentTargetId:N}";
            var prefix = BuildTargetRoutePrefix(route.AppId, route.DeploymentTargetId);
            var host = PublishedAppHostNames.BuildPrivateAppHost(
                route.AppId, publishedAppsConfig.PublicBaseUrlHost);
            AppendRuntimeRouter(builder, route, key, host, prefix, 110, true);
        }

        foreach (var route in routes.Where(route => !string.IsNullOrWhiteSpace(route.Slug)))
        {
            var key = $"slug-{route.AppId:N}";
            var host = PublishedAppHostNames.BuildPublishedAppHost(
                route.Slug!, publishedAppsConfig.PublicBaseUrlHost);
            var primary = PrimaryDomain(route);
            if (primary is null)
            {
                AppendRuntimeRouter(builder, route, key, host, "/", 50, route.RequiresAuthentication);
            }
            else
            {
                AppendRedirectRouter(builder, route, key, host, primary, publishedAppsConfig.CertificateResolver);
            }

            foreach (var domain in route.CustomDomains ?? [])
            {
                var domainKey = $"domain-{domain.Id:N}";
                if (primary is null || domain.Id == primary.Id)
                {
                    // The route reuses the app's public-address rewrite and sign-in middlewares.
                    AppendRuntimeRouter(builder, route, key, domain.Host, "/", 50, route.RequiresAuthentication,
                        routerName: domainKey, resolver: CustomDomainResolver());
                }
                else
                {
                    AppendRedirectRouter(builder, route, domainKey, domain.Host, primary, CustomDomainResolver());
                }
            }
        }
    }

    private static CustomDomainRoute? PrimaryDomain(RouteTarget route)
        => route.CustomDomains?.FirstOrDefault(domain => domain.IsPrimary);

    private string? CustomDomainResolver()
        => string.IsNullOrWhiteSpace(publishedAppsConfig.CustomDomainCertificateResolver)
            ? publishedAppsConfig.CertificateResolver
            : publishedAppsConfig.CustomDomainCertificateResolver;

    private void AppendRedirectRouter(
        StringBuilder builder, RouteTarget route, string key, string host, CustomDomainRoute primary, string? resolver)
    {
        builder.AppendLine($"    {key}-redirect:");
        builder.AppendLine("      entryPoints:");
        builder.AppendLine("        - websecure");
        builder.AppendLine(TlsLine(resolver));
        builder.AppendLine($"      rule: \"Host(`{host}`)\"");
        builder.AppendLine("      priority: 50");
        builder.AppendLine("      service: \"noop@internal\"");
        builder.AppendLine($"      middlewares: [\"redirect-{route.AppId:N}\"]");
    }

    private static string TlsLine(string? resolver)
        => string.IsNullOrWhiteSpace(resolver)
            ? "      tls: {}"
            : $"      tls: {{ certResolver: {System.Text.Json.JsonSerializer.Serialize(resolver)} }}";

    private void AppendMiddlewares(StringBuilder builder, IReadOnlyCollection<RouteTarget> routes)
    {
        builder.AppendLine("  middlewares:");
        // Routes the proxy does not authenticate (public apps, open paths) still drop any viewer headers the client sent.
        builder.AppendLine("    viewer-strip:");
        builder.AppendLine("      headers:");
        builder.AppendLine("        customRequestHeaders:");
        builder.AppendLine("          X-Automaton-Viewer-Role: \"\"");
        builder.AppendLine("          X-Automaton-Viewer-Token: \"\"");
        // The private address always asks for sign-in; the public address only when the app requires it.
        foreach (var route in routes)
        {
            var key = route.IsDefault
                ? $"private-{route.AppId:N}"
                : $"target-{route.DeploymentTargetId:N}";
            AppendRuntimeAuthorization(builder, route, key);

            if (route.RequiresAuthentication && !string.IsNullOrWhiteSpace(route.Slug))
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
            if (PrimaryDomain(route) is { } primary)
            {
                builder.AppendLine($"    redirect-{route.AppId:N}:");
                builder.AppendLine("      redirectRegex:");
                builder.AppendLine("        regex: \"^https?://[^/]+/(.*)$\"");
                builder.AppendLine($"        replacement: \"https://{primary.Host}/${{1}}\"");
                builder.AppendLine("        permanent: false");
            }
        }

        foreach (var route in routes.Where(route => route.Gateway is not null)
            .GroupBy(route => route.RuntimeServiceName).Select(group => group.First()))
        {
            builder.AppendLine($"    {route.RuntimeServiceName}-secret:");
            builder.AppendLine("      headers:");
            builder.AppendLine("        customRequestHeaders:");
            builder.AppendLine($"          {RunnerGatewayRegistry.SecretHeader}: {System.Text.Json.JsonSerializer.Serialize(route.Gateway!.Secret)}");
        }
    }

    private void AppendRuntimeRouter(
        StringBuilder builder,
        RouteTarget route,
        string key,
        string host,
        string pathPrefix,
        int priority,
        bool authenticated,
        string? pathRule = null,
        string? rewriteKey = null,
        string? routerName = null,
        string? resolver = null)
    {
        builder.AppendLine($"    {routerName ?? key}-runtime:");
        builder.AppendLine("      entryPoints:");
        builder.AppendLine("        - websecure");
        builder.AppendLine(TlsLine(resolver ?? publishedAppsConfig.CertificateResolver));
        builder.AppendLine($"      rule: \"Host(`{host}`) && {pathRule ?? $"PathPrefix(`{pathPrefix}`)"}\"");
        builder.AppendLine($"      priority: {priority}");
        builder.AppendLine($"      service: \"{route.RuntimeServiceName}\"");
        var rewrite = rewriteKey ?? $"{key}-rewrite";
        var secret = route.Gateway is null ? string.Empty : $", \"{route.RuntimeServiceName}-secret\"";
        var middlewares = authenticated
            ? $"[\"{key}-auth-headers\", \"{key}-auth\", \"{key}-auth-strip\", \"{rewrite}\"{secret}]"
            : $"[\"viewer-strip\", \"{rewrite}\"{secret}]";
        builder.AppendLine($"      middlewares: {middlewares}");
    }

    private void AppendRuntimeAuthorization(StringBuilder builder, RouteTarget route, string key)
    {
        builder.AppendLine($"    {key}-auth-headers:");
        builder.AppendLine("      headers:");
        builder.AppendLine("        customRequestHeaders:");
        builder.AppendLine($"          X-App-Id: \"{route.AppId}\"");
        builder.AppendLine($"          X-Automaton-Runtime-Auth: \"{publishedAppsConfig.RuntimeProxyAuthKey}\"");
        builder.AppendLine("          X-Automaton-Viewer-Role: \"\"");
        builder.AppendLine("          X-Automaton-Viewer-Token: \"\"");
        builder.AppendLine($"    {key}-auth:");
        builder.AppendLine("      forwardAuth:");
        builder.AppendLine($"        address: \"{publishedAppsConfig.ApiBaseUrl.TrimEnd('/')}/internal/runtime/auth\"");
        builder.AppendLine("        trustForwardHeader: true");
        builder.AppendLine("        authRequestHeaders:");
        builder.AppendLine("          - Cookie");
        builder.AppendLine("          - Accept");
        builder.AppendLine("          - X-App-Id");
        builder.AppendLine("          - X-Automaton-Runtime-Auth");
        builder.AppendLine("        authResponseHeaders:");
        builder.AppendLine("          - X-Automaton-Viewer-Role");
        builder.AppendLine("          - X-Automaton-Viewer-Token");
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

    // One service and one secret-header middleware per Runner that is served directly. The proxy sets the header, so a
    // browser cannot send it, and the gateway refuses everything else that is not the Runner's own tunnel.
    private static void AppendGatewayServices(StringBuilder builder, IReadOnlyCollection<RouteTarget> routes)
    {
        var direct = routes.Where(route => route.Gateway is not null)
            .GroupBy(route => route.RuntimeServiceName).Select(group => group.First()).ToList();
        if (direct.Count == 0) return;

        builder.AppendLine("  services:");
        foreach (var route in direct)
        {
            builder.AppendLine($"    {route.RuntimeServiceName}:");
            builder.AppendLine("      loadBalancer:");
            builder.AppendLine("        passHostHeader: true");
            builder.AppendLine("        servers:");
            builder.AppendLine($"          - url: \"{route.Gateway!.Url.ToString().TrimEnd('/')}\"");
        }
    }

    private static string BuildTargetRoutePrefix(Guid appId, Guid deploymentTargetId)
        => PublishedAppHostNames.BuildPrivateTargetPath(appId, deploymentTargetId, false).TrimEnd('/');

    private static string BuildRuntimePath(RouteTarget route, string path)
        => route.Gateway is not null
            ? $"/apps/{route.AppId}/targets/{route.DeploymentTargetId}/{path}"
            : $"/runtime-tunnel/apps/{route.AppId}/targets/{route.DeploymentTargetId}/{path}";

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
