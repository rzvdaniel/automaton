namespace Automaton.Api.Security;

/// <summary>Answers 429 to a request that carries an API key when its address has had too many keys rejected.</summary>
public sealed class ApiKeyFailureMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, ApiKeyFailureTracker tracker)
    {
        if (!HasApiKey(context.Request) || !tracker.IsBlocked(context.Connection.RemoteIpAddress?.ToString()))
            return next(context);

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = "60";
        context.Response.Headers[ApiKeyFailureTracker.HeaderName] = ApiKeyFailureTracker.HeaderValue;
        return context.Response.WriteAsJsonAsync(new { message = "Too many requests. Please try again later." });
    }

    private static bool HasApiKey(HttpRequest request)
        => request.Headers.ContainsKey("X-API-Key")
            || request.Query.ContainsKey("api_key")
            || request.Headers.Authorization.FirstOrDefault()?.StartsWith("Bearer ak_", StringComparison.Ordinal) == true;
}
