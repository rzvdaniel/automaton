using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace Automaton.Runner.Console.Services;

/// <summary>Proxies deployed application traffic to the healthy runtime process.</summary>
public sealed class RunnerRuntimeGatewayService : IHostedService
{
    private readonly RunnerRuntimeRegistry runtimeRegistry;
    private readonly HttpClient runtimeClient;
    private readonly ILogger<RunnerRuntimeGatewayService> logger;
    private WebApplication? application;

    public RunnerRuntimeGatewayService(
        RunnerRuntimeRegistry runtimeRegistry,
        HttpClient runtimeClient,
        ILogger<RunnerRuntimeGatewayService> logger)
    {
        this.runtimeRegistry = runtimeRegistry;
        this.runtimeClient = runtimeClient;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(GetListenUrl());
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
        app.MapGet("/ready", () => Results.Ok(new { status = "ready" }));
        app.Map("/apps", branch => branch.Run(ProxyRuntimePathAsync));

        application = app;
        await app.StartAsync(cancellationToken);
        logger.LogInformation("Runner runtime gateway listening on {RuntimeUrl}", GetListenUrl());
    }

    private async Task ProxyRuntimePathAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 ||
            !Guid.TryParse(segments[0], out var appId) ||
            !string.Equals(segments[1], "targets", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(segments[2], out var deploymentTargetId))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var targetMarker = $"/{appId:D}/targets/{deploymentTargetId:D}";
        var markerIndex = path.IndexOf(targetMarker, StringComparison.OrdinalIgnoreCase);
        var runtimePath = markerIndex >= 0
            ? path[(markerIndex + targetMarker.Length)..]
            : string.Empty;
        await ProxyRuntimeAsync(context, appId, deploymentTargetId, runtimePath);
    }

    private async Task ProxyRuntimeAsync(
        HttpContext context,
        Guid appId,
        Guid deploymentTargetId,
        string? path)
    {
        if (!runtimeRegistry.TryGet(appId, deploymentTargetId, out var runtime))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Application runtime is unavailable." });
            return;
        }

        var target = new UriBuilder("http", "127.0.0.1", runtime.Port)
        {
            Path = string.IsNullOrEmpty(path) ? "/" : path.StartsWith('/') ? path : "/" + path,
            Query = context.Request.QueryString.Value?.TrimStart('?') ?? string.Empty
        }.Uri;

        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
            request.Content = new StreamContent(context.Request.Body);
        var requestConnectionTokens = GetConnectionTokens(context.Request.Headers);
        foreach (var header in context.Request.Headers)
        {
            if (IsHopByHopHeader(header.Key, requestConnectionTokens) ||
                string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        try
        {
            using var response = await runtimeClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            var responseConnectionTokens = GetConnectionTokens(response.Headers);
            foreach (var header in response.Headers)
            {
                if (!IsHopByHopHeader(header.Key, responseConnectionTokens))
                    context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            foreach (var header in response.Content.Headers)
            {
                if (!IsHopByHopHeader(header.Key, responseConnectionTokens))
                    context.Response.Headers[header.Key] = header.Value.ToArray();
            }
            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (HttpRequestException)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsJsonAsync(new { error = "Application runtime could not be reached." }, context.RequestAborted);
            }
        }
    }

    private static HashSet<string> GetConnectionTokens(IHeaderDictionary headers)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in headers["Connection"])
        {
            foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                tokens.Add(token);
        }

        return tokens;
    }

    private static HashSet<string> GetConnectionTokens(HttpResponseHeaders headers)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in headers.Connection)
        {
            foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                tokens.Add(token);
        }

        return tokens;
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

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (application is not null)
        {
            await application.StopAsync(cancellationToken);
            await application.DisposeAsync();
            application = null;
        }
    }

    private static string GetListenUrl()
    {
        var port = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT");
        return int.TryParse(port, out var parsedPort) && parsedPort is > 0 and <= 65535
            ? $"http://0.0.0.0:{parsedPort}"
            : "http://0.0.0.0:8080";
    }
}
