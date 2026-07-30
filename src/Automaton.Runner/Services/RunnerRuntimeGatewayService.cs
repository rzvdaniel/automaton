using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Mime;

namespace Automaton.Runner.Console.Services;

/// <summary>Hosts deployed frontend runtimes directly from the runner workspace.</summary>
public sealed class RunnerRuntimeGatewayService : IHostedService
{
    private readonly RunnerWorkspaceService workspace;
    private readonly RunnerBackendRegistry backendRegistry;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<RunnerRuntimeGatewayService> logger;
    private WebApplication? application;

    public RunnerRuntimeGatewayService(
        RunnerWorkspaceService workspace,
        RunnerBackendRegistry backendRegistry,
        IHttpClientFactory httpClientFactory,
        ILogger<RunnerRuntimeGatewayService> logger)
    {
        this.workspace = workspace;
        this.backendRegistry = backendRegistry;
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
        app.MapGet("/ready", () => Results.Ok(new { status = "ready", workspace = workspace.WorkspaceRoot }));
        app.MapMethods(
            "/apps/{appId:guid}/targets/{deploymentTargetId:guid}/api/{**path}",
            new[] { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" },
            ProxyApiAsync);
        app.MapMethods(
            "/apps/{appId:guid}/targets/{deploymentTargetId:guid}/{**path}",
            new[] { "GET", "HEAD" },
            ServeFrontendAsync);
        app.MapMethods(
            "/apps/{appId:guid}/targets/{deploymentTargetId:guid}",
            new[] { "GET", "HEAD" },
            (Guid appId, Guid deploymentTargetId) =>
                ServeFrontendAsync(appId, deploymentTargetId, null));

        application = app;
        await app.StartAsync(cancellationToken);
        logger.LogInformation("Runner runtime gateway listening on {RuntimeUrl}", GetListenUrl());
    }

    private async Task ProxyApiAsync(
        HttpContext context,
        Guid appId,
        Guid deploymentTargetId,
        string? path)
    {
        if (!backendRegistry.TryGet(appId, deploymentTargetId, out var backend))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "Backend runtime is unavailable." });
            return;
        }

        var target = new UriBuilder("http", "127.0.0.1", backend.Port)
        {
            // The gateway route removes only /apps/{appId}; preserve /api for the app backend.
            Path = "/api/" + (path ?? string.Empty),
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
                await context.Response.WriteAsJsonAsync(new { error = "Backend runtime could not be reached." }, context.RequestAborted);
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

    private async Task<IResult> ServeFrontendAsync(
        Guid appId,
        Guid deploymentTargetId,
        string? path)
    {
        var root = workspace.GetAppRuntimeFrontendDirectory(appId, deploymentTargetId);
        if (!Directory.Exists(root))
        {
            return Results.NotFound(new { error = "Frontend runtime is not deployed." });
        }

        var relativePath = string.IsNullOrWhiteSpace(path) ? "index.html" : path;
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithinRoot(root, candidate))
        {
            return Results.BadRequest(new { error = "Invalid runtime path." });
        }

        if (!File.Exists(candidate))
        {
            if (relativePath.Contains("/api/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            {
                return Results.NotFound();
            }

            candidate = Path.Combine(root, "index.html");
            if (!File.Exists(candidate))
            {
                return Results.NotFound(new { error = "Frontend entry point index.html is missing." });
            }
        }

        var contentType = new FileExtensionContentTypeProvider().TryGetContentType(candidate, out var type)
            ? type
            : MediaTypeNames.Application.Octet;
        var result = Results.File(candidate, contentType, enableRangeProcessing: true);
        return await Task.FromResult(result);
    }

    private static bool IsWithinRoot(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidate, normalizedRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetListenUrl()
    {
        var port = Environment.GetEnvironmentVariable("AUTOMATON_RUNTIME_HTTP_PORT");
        return int.TryParse(port, out var parsedPort) && parsedPort is > 0 and <= 65535
            ? $"http://0.0.0.0:{parsedPort}"
            : "http://0.0.0.0:8080";
    }
}
