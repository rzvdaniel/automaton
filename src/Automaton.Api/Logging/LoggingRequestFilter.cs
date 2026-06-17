namespace Automaton.Api.Logging;

internal static class LoggingRequestFilter
{
    public static bool ShouldSkipRequestLogging(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments("/healthz")
            || HttpMethods.IsOptions(context.Request.Method);
    }
}
