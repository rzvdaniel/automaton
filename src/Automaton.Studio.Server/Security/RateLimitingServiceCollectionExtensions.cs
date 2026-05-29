using Automaton.Studio.Server.Constants;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace Automaton.Studio.Server.Security;

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

        services.AddRateLimiter(options =>
        {
            var rateLimitingOptions = services
                .BuildServiceProvider()
                .GetRequiredService<IOptions<RateLimitingOptions>>()
                .Value;

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var identity = httpContext.User.Identity;
                if (identity?.IsAuthenticated == true)
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        GetAuthenticatedPartitionKey(httpContext),
                        _ => CreatePolicyOptions(rateLimitingOptions.GlobalAuthenticated));
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, "global:anonymous"),
                    _ => CreatePolicyOptions(rateLimitingOptions.GlobalAnonymous));
            });
            options.OnRejected = (context, cancellationToken) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
                    new { message = "Too many requests. Please try again later." },
                    cancellationToken));
            };

            options.AddPolicy(RateLimitPolicyNames.AuthLogin, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.AuthLogin),
                    _ => CreatePolicyOptions(rateLimitingOptions.AuthLogin)));

            options.AddPolicy(RateLimitPolicyNames.AuthRegistration, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.AuthRegistration),
                    _ => CreatePolicyOptions(rateLimitingOptions.AuthRegistration)));

            options.AddPolicy(RateLimitPolicyNames.AuthForgotPassword, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.AuthForgotPassword),
                    _ => CreatePolicyOptions(rateLimitingOptions.AuthForgotPassword)));

            options.AddPolicy(RateLimitPolicyNames.AuthResetPassword, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.AuthResetPassword),
                    _ => CreatePolicyOptions(rateLimitingOptions.AuthResetPassword)));

            options.AddPolicy(RateLimitPolicyNames.AuthRefreshToken, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.AuthRefreshToken),
                    _ => CreatePolicyOptions(rateLimitingOptions.AuthRefreshToken)));

            options.AddPolicy(RateLimitPolicyNames.ApiKeyValidation, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.ApiKeyValidation),
                    _ => CreatePolicyOptions(rateLimitingOptions.ApiKeyValidation)));

            options.AddPolicy(RateLimitPolicyNames.PublicAppControllerExecution, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.PublicAppControllerExecution),
                    _ => CreatePolicyOptions(rateLimitingOptions.PublicAppControllerExecution)));

            options.AddPolicy(RateLimitPolicyNames.PublicAppFlowExecution, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.PublicAppFlowExecution),
                    _ => CreatePolicyOptions(rateLimitingOptions.PublicAppFlowExecution)));

            options.AddPolicy(RateLimitPolicyNames.PublicAppStepExecution, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.PublicAppStepExecution),
                    _ => CreatePolicyOptions(rateLimitingOptions.PublicAppStepExecution)));
        });

        return services;
    }

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
