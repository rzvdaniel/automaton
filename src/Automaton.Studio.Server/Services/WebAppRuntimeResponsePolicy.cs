namespace Automaton.Api.Services;

public static class WebAppRuntimeResponsePolicy
{
    public static void ApplyPublicAssetCorsHeaders(HttpResponse response)
    {
        response.Headers.AccessControlAllowOrigin = "*";
    }
}
