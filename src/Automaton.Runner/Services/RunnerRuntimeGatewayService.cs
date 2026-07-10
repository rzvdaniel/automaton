using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Net;
using System.Net.Mime;

namespace Automaton.Runner.Console.Services;

/// <summary>Hosts deployed frontend runtimes directly from the runner workspace.</summary>
public sealed class RunnerRuntimeGatewayService : IHostedService
{
    private readonly RunnerWorkspaceService workspace;
    private readonly ILogger<RunnerRuntimeGatewayService> logger;
    private WebApplication? application;

    public RunnerRuntimeGatewayService(
        RunnerWorkspaceService workspace,
        ILogger<RunnerRuntimeGatewayService> logger)
    {
        this.workspace = workspace;
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
        app.MapMethods("/apps/{appId:guid}/{**path}", new[] { "GET", "HEAD" }, ServeFrontendAsync);
        app.MapMethods("/apps/{appId:guid}", new[] { "GET", "HEAD" }, (Guid appId) => ServeFrontendAsync(appId, null));

        application = app;
        await app.StartAsync(cancellationToken);
        logger.LogInformation("Runner runtime gateway listening on {RuntimeUrl}", GetListenUrl());
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

    private async Task<IResult> ServeFrontendAsync(Guid appId, string? path)
    {
        var root = workspace.GetAppRuntimeFrontendDirectory(appId);
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
