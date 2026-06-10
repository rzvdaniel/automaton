namespace Automaton.Studio.Server.Logging;

internal static class LoggingRequestFilter
{
    public static bool ShouldSkipRequestLogging(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments("/healthz")
            || HttpMethods.IsOptions(context.Request.Method);
    }
}
