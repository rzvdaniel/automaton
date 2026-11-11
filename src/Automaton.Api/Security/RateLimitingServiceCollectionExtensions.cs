using Automaton.Api.Constants;
using Automaton.Api.Middleware;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace Automaton.Api.Security;

public static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddAutomatonRateLimiting(this IServiceCollection services)
    {
        services.AddMemoryCache();

        services
            .AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<RateLimitingOptions>, RateLimitingOptionsValidation>();
        services.AddSingleton<MultipartUploadAdmission>();

        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingOptions>>((options, configuredOptions) =>
                ConfigureRateLimiterOptions(options, configuredOptions.Value));

        services.AddRateLimiter();

        return services;
    }

    private static void ConfigureRateLimiterOptions(RateLimiterOptions options, RateLimitingOptions configuredOptions)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.GlobalLimiter = CreateGlobalLimiter(configuredOptions);
        options.OnRejected = WriteRateLimitRejectionAsync;
        AddAuthenticationPolicies(options, configuredOptions);
        AddApplicationPolicies(options, configuredOptions);
    }

    private static PartitionedRateLimiter<HttpContext> CreateGlobalLimiter(RateLimitingOptions options)
        => PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            // An app's own scripts, styles and images get their own, larger bucket so loading one page cannot
            // use up the visitor's general allowance. Everything else, including the API, keeps the normal limits.
            if (IsAppStaticAssetRequest(httpContext.Request))
            {
                return RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, "global:app-assets"),
                    _ => CreatePolicyOptions(options.AppStaticAssets));
            }

            // A Runner's own calls (result polling, status, health) carry an API key that authentication has already accepted, and come from the proxy's address,
            // the same address every anonymous visitor shares. They get their own bucket so a busy Runner is not refused
            // because of other traffic, and cannot use up the visitors' allowance.
            if (IsRunnerApiRequest(httpContext.Request) && RateLimitedApiKeys.Recognise(httpContext) is { } runnerKey)
            {
                return RateLimitPartition.GetFixedWindowLimiter(
                    "global:runner-api:" + runnerKey,
                    _ => CreatePolicyOptions(options.RunnerApi));
            }

            var identity = httpContext.User.Identity;
            if (identity?.IsAuthenticated == true)
            {
                return RateLimitPartition.GetFixedWindowLimiter(
                    GetAuthenticatedPartitionKey(httpContext),
                    _ => CreatePolicyOptions(options.GlobalAuthenticated));
            }

            return RateLimitPartition.GetFixedWindowLimiter(
                GetRemoteIpPartitionKey(httpContext, "global:anonymous"),
                _ => CreatePolicyOptions(options.GlobalAnonymous));
        });

    private static readonly string[] RunnerApiPrefixes =
        ["/api/host-container-executions", "/api/runner-executions", "/api/server/health"];

    /// <summary>A call to a Runner endpoint that presents an API key. Only keys authentication has accepted get the bucket.</summary>
    internal static bool IsRunnerApiRequest(HttpRequest request)
        => request.Headers.ContainsKey("X-API-Key")
            && RunnerApiPrefixes.Any(prefix => request.Path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private static readonly HashSet<string> StaticAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".css", ".map", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".avif", ".ico",
        ".woff", ".woff2", ".ttf", ".otf", ".wasm", ".webmanifest"
    };

    /// <summary>A GET or HEAD for a static file through an app's runtime tunnel (an app address, never the platform API).</summary>
    internal static bool IsAppStaticAssetRequest(HttpRequest request)
    {
        // A private app's request first passes the proxy's sign-in check (/internal/runtime/auth, which only answers with the
        // proxy's key). The proxy may not pass the original URI on, so every such check counts here, apart from the API.
        var path = request.Path;
        var target = path.Value ?? string.Empty;
        if (path.StartsWithSegments("/internal/runtime/auth", StringComparison.Ordinal)) return true;
        return (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
            && path.StartsWithSegments("/runtime-tunnel/apps", StringComparison.Ordinal)
            && StaticAssetExtensions.Contains(Path.GetExtension(target));
    }

    private static ValueTask WriteRateLimitRejectionAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many requests. Please try again later." },
            cancellationToken));
    }

    private static void AddAuthenticationPolicies(RateLimiterOptions options, RateLimitingOptions configuredOptions)
    {
        AddRemoteIpPolicy(options, RateLimitPolicyNames.AuthLogin, configuredOptions.AuthLogin);
        AddRemoteIpPolicy(options, RateLimitPolicyNames.AuthRegistration, configuredOptions.AuthRegistration);
        AddRemoteIpPolicy(options, RateLimitPolicyNames.AuthForgotPassword, configuredOptions.AuthForgotPassword);
        AddRemoteIpPolicy(options, RateLimitPolicyNames.AuthResendConfirmation, configuredOptions.AuthResendConfirmation);
        AddRemoteIpPolicy(options, RateLimitPolicyNames.AuthResetPassword, configuredOptions.AuthResetPassword);
        AddRemoteIpPolicy(options, RateLimitPolicyNames.AuthRefreshToken, configuredOptions.AuthRefreshToken);
    }

    private static void AddApplicationPolicies(RateLimiterOptions options, RateLimitingOptions configuredOptions)
    {
        AddRemoteIpPolicy(options, RateLimitPolicyNames.ApiKeyValidation, configuredOptions.ApiKeyValidation);
        AddRemoteIpPolicy(options, RateLimitPolicyNames.PublicAppControllerExecution, configuredOptions.PublicAppControllerExecution);
        AddRemoteIpPolicy(options, RateLimitPolicyNames.PublicAppFlowExecution, configuredOptions.PublicAppFlowExecution);
        AddRemoteIpPolicy(options, RateLimitPolicyNames.PublicAppStepExecution, configuredOptions.PublicAppStepExecution);
        options.AddPolicy(RateLimitPolicyNames.PublicMarketplaceAppLaunch, context =>
        {
            var appId = context.Request.RouteValues["appId"]?.ToString() ?? "unknown";
            var partitionKey = GetRemoteIpPartitionKey(
                context,
                $"{RateLimitPolicyNames.PublicMarketplaceAppLaunch}:{appId}");
            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey,
                _ => CreatePolicyOptions(configuredOptions.PublicMarketplaceAppLaunch));
        });
    }

    private static void AddRemoteIpPolicy(
        RateLimiterOptions options,
        string name,
        RateLimitPolicyOptions policy)
        => options.AddPolicy(name, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                GetRemoteIpPartitionKey(context, name),
                _ => CreatePolicyOptions(policy)));

    private static FixedWindowRateLimiterOptions CreatePolicyOptions(RateLimitPolicyOptions policy)
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = policy.PermitLimit,
            Window = TimeSpan.FromSeconds(policy.WindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        };
    }

    private static string GetRemoteIpPartitionKey(HttpContext httpContext, string policyName)
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
        return $"{policyName}:{remoteIp ?? "unknown"}";
    }

    private static string GetAuthenticatedPartitionKey(HttpContext httpContext)
    {
        var user = httpContext.User;
        var userId =
            user.FindFirstValue(ClaimTypes.NameIdentifier) ??
            user.FindFirstValue("sub") ??
            user.Identity?.Name;

        return $"global:authenticated:{userId ?? "unknown"}";
    }
}
