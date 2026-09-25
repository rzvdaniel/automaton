using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automaton.Runner.Console.Services;

/// <summary>Proxies deployed application traffic to the healthy runtime process.</summary>
public sealed class RunnerRuntimeGatewayService : IHostedService
{
    private readonly RunnerRuntimeGatewayProxy proxy;
    private readonly ILogger<RunnerRuntimeGatewayService> logger;
    private WebApplication? application;

    public RunnerRuntimeGatewayService(
        RunnerRuntimeRegistry runtimeRegistry,
        HttpClient runtimeClient,
        ILogger<RunnerRuntimeGatewayService> logger)
    {
        proxy = new RunnerRuntimeGatewayProxy(runtimeRegistry, runtimeClient);
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
        await proxy.ProxyAsync(context, appId, deploymentTargetId, runtimePath);
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
        return $"http://0.0.0.0:{RunnerPlatformPorts.GetRuntimeGatewayPort()}";
    }
}
