using Automaton.Api.Constants;
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
