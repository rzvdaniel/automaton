using Automaton.Studio.Server.Constants;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace Automaton.Studio.Server.Security;

public static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddAutomatonRateLimiting(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
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
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(RateLimitPolicyNames.AuthRegistration, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.AuthRegistration),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(RateLimitPolicyNames.AuthForgotPassword, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.AuthForgotPassword),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(RateLimitPolicyNames.AuthResetPassword, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.AuthResetPassword),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(RateLimitPolicyNames.AuthRefreshToken, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.AuthRefreshToken),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(RateLimitPolicyNames.ApiKeyValidation, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetRemoteIpPartitionKey(httpContext, RateLimitPolicyNames.ApiKeyValidation),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    private static string GetRemoteIpPartitionKey(HttpContext httpContext, string policyName)
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString();
        return $"{policyName}:{remoteIp ?? "unknown"}";
    }
}
