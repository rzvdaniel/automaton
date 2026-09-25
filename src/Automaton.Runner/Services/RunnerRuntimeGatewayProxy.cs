using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Automaton.Runner.Console.Services;

internal sealed class RunnerRuntimeGatewayProxy(RunnerRuntimeRegistry runtimeRegistry, HttpClient runtimeClient)
{
    private const string RuntimeCookiePrefix = "automaton_runtime_";

    public async Task ProxyAsync(HttpContext context, Guid appId, Guid deploymentTargetId, string? path)
    {
        if (!runtimeRegistry.TryGet(appId, deploymentTargetId, out var runtime))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(
                new { error = "Application runtime is unavailable." },
                context.RequestAborted);
            return;
        }

        using var request = CreateRequest(context, runtime.Port, path);
        try
        {
            using var response = await runtimeClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            await CopyResponseAsync(context, response);
        }
        catch (HttpRequestException)
        {
            await WriteUnavailableResponseAsync(context);
        }
    }

    private static HttpRequestMessage CreateRequest(HttpContext context, int port, string? path)
    {
        var target = new UriBuilder("http", RunnerRuntimeEndpointConfiguration.HostAddress, port)
        {
            Path = NormalizePath(path),
            Query = context.Request.QueryString.Value?.TrimStart('?') ?? string.Empty
        }.Uri;
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
            request.Content = new StreamContent(context.Request.Body);

        var connectionTokens = GetConnectionTokens(context.Request.Headers["Connection"]);
        foreach (var header in context.Request.Headers)
            CopyRequestHeader(request, header.Key, header.Value, connectionTokens);
        return request;
    }

    private static void CopyRequestHeader(
        HttpRequestMessage request,
        string name,
        IEnumerable<string> values,
        ISet<string> connectionTokens)
    {
        if (IsHopByHopHeader(name, connectionTokens)
            || string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
            return;

        if (name.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
        {
            var cookies = FilterRuntimeCookies(values);
            if (cookies is not null) request.Headers.TryAddWithoutValidation("Cookie", cookies);
            return;
        }

        var headerValues = values.ToArray();
        if (!request.Headers.TryAddWithoutValidation(name, headerValues))
            request.Content?.Headers.TryAddWithoutValidation(name, headerValues);
    }

    private static async Task CopyResponseAsync(HttpContext context, HttpResponseMessage response)
    {
        context.Response.StatusCode = (int)response.StatusCode;
        var connectionTokens = GetConnectionTokens(response.Headers.Connection);
        CopyResponseHeaders(response.Headers, context.Response.Headers, connectionTokens);
        CopyResponseHeaders(response.Content.Headers, context.Response.Headers, connectionTokens);
        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static void CopyResponseHeaders(
        IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers,
        IHeaderDictionary destination,
        ISet<string> connectionTokens)
    {
        foreach (var header in headers.Where(header => !IsHopByHopHeader(header.Key, connectionTokens)))
            destination[header.Key] = header.Value.ToArray();
    }

    private static async Task WriteUnavailableResponseAsync(HttpContext context)
    {
        if (context.Response.HasStarted) return;
        context.Response.StatusCode = StatusCodes.Status502BadGateway;
        await context.Response.WriteAsJsonAsync(
            new { error = "Application runtime could not be reached." },
            context.RequestAborted);
    }

    private static HashSet<string> GetConnectionTokens(IEnumerable<string> values)
        => values.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        return path.StartsWith('/') ? path : "/" + path;
    }

    private static string? FilterRuntimeCookies(IEnumerable<string> cookieHeaders)
    {
        var applicationCookies = cookieHeaders
            .SelectMany(value => value.Split(';'))
            .Select(cookie => cookie.Trim())
            .Where(cookie => cookie.Length > 0)
            .Where(cookie => !IsRuntimeCookie(cookie))
            .ToArray();
        return applicationCookies.Length == 0 ? null : string.Join("; ", applicationCookies);
    }

    private static bool IsRuntimeCookie(string cookie)
    {
        var separator = cookie.IndexOf('=');
        var name = separator >= 0 ? cookie[..separator].Trim() : cookie;
        return name.StartsWith(RuntimeCookiePrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHopByHopHeader(string name, ISet<string> connectionTokens) =>
        connectionTokens.Contains(name) || name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authenticate", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("TE", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);
}
