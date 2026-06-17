using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;

namespace Automaton.Api.Middleware;

public class EmailRateLimitFilter : IAsyncActionFilter
{
    private readonly IMemoryCache memoryCache;
    private readonly ILogger<EmailRateLimitFilter> logger;
    private readonly string keyPrefix;
    private readonly int permitLimit;
    private readonly TimeSpan window;

    public EmailRateLimitFilter(
        IMemoryCache memoryCache,
        ILogger<EmailRateLimitFilter> logger,
        string keyPrefix,
        int permitLimit,
        int windowSeconds)
    {
        this.memoryCache = memoryCache;
        this.logger = logger;
        this.keyPrefix = keyPrefix;
        this.permitLimit = permitLimit;
        window = TimeSpan.FromSeconds(windowSeconds);
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var email = ExtractEmail(context.ActionArguments.Values);
        if (string.IsNullOrWhiteSpace(email))
        {
            await next();
            return;
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var cacheKey = $"{keyPrefix}:{normalizedEmail}";
        var counter = memoryCache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = window;
            return new EmailRateLimitCounter();
        })!;

        lock (counter)
        {
            counter.Count++;

            if (counter.Count > permitLimit)
            {
                logger.LogWarning(
                    "Email rate limit exceeded for policy {Policy} and email {Email}",
                    keyPrefix,
                    normalizedEmail);

                context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(window.TotalSeconds)).ToString();
                context.Result = new ObjectResult(new { message = "Too many requests. Please try again later." })
                {
                    StatusCode = StatusCodes.Status429TooManyRequests
                };
            }
        }

        if (context.Result != null)
        {
            return;
        }

        await next();
    }

    private static string? ExtractEmail(IEnumerable<object?> actionArguments)
    {
        foreach (var argument in actionArguments)
        {
            if (argument == null)
            {
                continue;
            }

            var emailProperty = argument.GetType().GetProperty("Email");
            if (emailProperty?.PropertyType != typeof(string))
            {
                continue;
            }

            return emailProperty.GetValue(argument) as string;
        }

        return null;
    }

    private sealed class EmailRateLimitCounter
    {
        public int Count { get; set; }
    }
}

public sealed class EmailRateLimitAttribute : TypeFilterAttribute
{
    public EmailRateLimitAttribute(string keyPrefix, int permitLimit, int windowSeconds)
        : base(typeof(EmailRateLimitFilter))
    {
        Arguments = [keyPrefix, permitLimit, windowSeconds];
    }
}
