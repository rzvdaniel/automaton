using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

/// <summary>Proxies deployed application traffic to the healthy runtime process.</summary>
public sealed class RunnerRuntimeGatewayService : IHostedService
{
    private readonly RunnerRuntimeRegistry runtimeRegistry;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<RunnerRuntimeGatewayService> logger;
    private WebApplication? application;

    public RunnerRuntimeGatewayService(
        RunnerRuntimeRegistry runtimeRegistry,
        IHttpClientFactory httpClientFactory,
        ILogger<RunnerRuntimeGatewayService> logger)
    {
        this.runtimeRegistry = runtimeRegistry;
        this.httpClientFactory = httpClientFactory;
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
        app.MapMethods(
            "/apps/{appId:guid}/targets/{deploymentTargetId:guid}/{**path}",
            new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" },
            ProxyRuntimeAsync);
        app.MapMethods(
            "/apps/{appId:guid}/targets/{deploymentTargetId:guid}",
            new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" },
            (HttpContext context, Guid appId, Guid deploymentTargetId) =>
                ProxyRuntimeAsync(context, appId, deploymentTargetId, null));

        application = app;
        await app.StartAsync(cancellationToken);
        logger.LogInformation("Runner runtime gateway listening on {RuntimeUrl}", GetListenUrl());
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
            Path = string.IsNullOrWhiteSpace(path) ? "/" : "/" + path,
            Query = context.Request.QueryString.Value?.TrimStart('?') ?? string.Empty
        }.Uri;

        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            request.Content = new StreamContent(context.Request.Body);
        foreach (var header in context.Request.Headers)
        {
            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        try
        {
            using var response = await httpClientFactory.CreateClient().SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers)
                context.Response.Headers[header.Key] = header.Value.ToArray();
            foreach (var header in response.Content.Headers)
                context.Response.Headers[header.Key] = header.Value.ToArray();
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
