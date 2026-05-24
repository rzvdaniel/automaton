namespace Automaton.Studio.Server.Services;

public static class WebAppRuntimeResponsePolicy
{
    public static void ApplyPublicAssetCorsHeaders(HttpResponse response)
    {
        response.Headers.AccessControlAllowOrigin = "*";
    }
}
