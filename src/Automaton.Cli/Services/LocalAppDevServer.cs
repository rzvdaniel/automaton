using Automaton.Cli.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Automaton.Cli.Services;

public class LocalAppDevServer(LocalAppExecutionService executionService)
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();
    private static readonly string[] BlockedPathPrefixes = [".automaton", ".git", "backend", "data", "node_modules", "obj", "bin"];

    public async Task RunAsync(LocalAppContext context, LocalAppDevServerOptions options, CancellationToken cancellationToken = default)
    {
        var port = options.Port ?? FindAvailablePort(3010, 25);
        var previewUrl = $"http://{options.Host}:{port}";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = context.RootPath,
            ApplicationName = typeof(LocalAppDevServer).Assembly.FullName
        });

        builder.WebHost.UseUrls(previewUrl);
        var app = builder.Build();
        var reloadNotifier = new ReloadNotifier();

        using var frontendWatcher = CreateWatcher(
            context.FrontendPath,
            changedPath =>
            {
                if (!ShouldTriggerReload(context, changedPath))
                {
                    return;
                }

                reloadNotifier.Notify();
                if (options.Verbose)
                {
                    AnsiConsole.MarkupLine($"[grey]Frontend changed:[/] {Path.GetRelativePath(context.RootPath, changedPath)}");
                }
            });

        using var backendWatcher = Directory.Exists(context.BackendPath)
            ? CreateWatcher(
                context.BackendPath,
                changedPath =>
                {
                    if (options.Verbose)
                    {
                        AnsiConsole.MarkupLine($"[grey]Backend changed:[/] {Path.GetRelativePath(context.RootPath, changedPath)}");
                    }
                })
            : null;

        app.MapPost("/__automaton/controller/{controller}/{action}", async (HttpContext httpContext, string controller, string action) =>
        {
            using var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: httpContext.RequestAborted);
            var inputsJson = body.RootElement.TryGetProperty("inputs", out var inputsProperty)
                ? inputsProperty.GetRawText()
                : "{}";

            var result = await executionService.ExecuteAsync(context, new LocalAppExecutionOptions
            {
                ControllerName = controller,
                ActionName = action,
                InputsJson = inputsJson,
                Mode = options.Mode,
                Timeout = TimeSpan.FromSeconds(30),
                Verbose = options.Verbose
            }, httpContext.RequestAborted);

            LogExecution(result, options.Verbose);
            return Results.Json(new { success = result.Success, data = result.Data, error = result.Error });
        });

        app.MapGet("/__automaton/bridge.js", () =>
        {
            return Results.Text(BuildBridgeScript(options.Mode), "application/javascript", Encoding.UTF8);
        });

        app.MapGet("/__automaton/events", async httpContext =>
        {
            httpContext.Response.Headers.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            while (!httpContext.RequestAborted.IsCancellationRequested)
            {
                await reloadNotifier.WaitForChangeAsync(httpContext.RequestAborted);
                await httpContext.Response.WriteAsync("data: reload\n\n", httpContext.RequestAborted);
                await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
            }
        });

        app.MapMethods("/{**path}", ["GET", "HEAD"], async (HttpContext httpContext, string? path) =>
        {
            var response = await ServeFrontendAsync(context, path, httpContext.RequestAborted);
            httpContext.Response.StatusCode = response.StatusCode;

            if (!string.IsNullOrWhiteSpace(response.ContentType))
            {
                httpContext.Response.ContentType = response.ContentType;
            }

            if (response.Bytes is not null)
            {
                await httpContext.Response.Body.WriteAsync(response.Bytes, httpContext.RequestAborted);
            }
        });

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            PrintStartupSummary(context, options, previewUrl);
            if (options.OpenBrowser)
            {
                TryOpenBrowser(previewUrl);
            }
        });

        await app.RunAsync(cancellationToken);
    }

    private static async Task<FrontendResponse> ServeFrontendAsync(LocalAppContext context, string? requestPath, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeRequestPath(requestPath);
        if (IsBlockedPath(normalizedPath))
        {
            return FrontendResponse.NotFound();
        }

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return await ServeIndexHtmlAsync(context, cancellationToken);
        }

        var filePath = Path.GetFullPath(Path.Combine(context.FrontendPath, normalizedPath));
        if (!filePath.StartsWith(Path.GetFullPath(context.FrontendPath), StringComparison.OrdinalIgnoreCase))
        {
            return FrontendResponse.NotFound();
        }

        if (File.Exists(filePath))
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return new FrontendResponse(200, ResolveContentType(filePath), bytes);
        }

        return Path.HasExtension(normalizedPath)
            ? FrontendResponse.NotFound()
            : await ServeIndexHtmlAsync(context, cancellationToken);
    }

    private static async Task<FrontendResponse> ServeIndexHtmlAsync(LocalAppContext context, CancellationToken cancellationToken)
    {
        if (!File.Exists(context.IndexHtmlPath))
        {
            return new FrontendResponse(500, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("index.html not found."));
        }

        var html = await File.ReadAllTextAsync(context.IndexHtmlPath, cancellationToken);
        var injection = "<script src=\"/__automaton/bridge.js\"></script>";
        if (!html.Contains("/__automaton/bridge.js", StringComparison.Ordinal))
        {
            html = html.Contains("</head>", StringComparison.OrdinalIgnoreCase)
                ? html.Replace("</head>", $"{injection}</head>", StringComparison.OrdinalIgnoreCase)
                : html.Contains("</body>", StringComparison.OrdinalIgnoreCase)
                    ? html.Replace("</body>", $"{injection}</body>", StringComparison.OrdinalIgnoreCase)
                    : html + injection;
        }

        return new FrontendResponse(200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
    }

    private static string BuildBridgeScript(string mode)
    {
        return $$"""
            (() => {
              if (window.automatonApi) {
                return;
              }

              const backendMode = {{JsonSerializer.Serialize(mode)}};
              const reloadUrl = '/__automaton/events';

              if (typeof window.EventSource === 'function') {
                const source = new EventSource(reloadUrl);
                source.onmessage = () => window.location.reload();
              }

              async function callController(controllerName, actionName, inputs = {}) {
                const response = await fetch(`/__automaton/controller/${encodeURIComponent(controllerName)}/${encodeURIComponent(actionName)}`, {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json' },
                  body: JSON.stringify({ inputs })
                });

                const result = await response.json();
                if (!response.ok || !result.success) {
                  throw new Error(result.error || `Controller call failed for ${controllerName}.${actionName}`);
                }

                return result.data;
              }

              window.automatonApi = {
                callController,
                executeFlow: async () => ({ success: true, status: 'Completed', data: {} }),
                uploadFile: async (file) => ({ session_id: `dev-${Date.now()}`, fileName: file?.name ?? null }),
                downloadFile: async () => { throw new Error('downloadFile is not available in local app dev mode.'); },
                getBackendMode: () => backendMode,
                setBackendMode: () => backendMode,
                getAppId: () => null,
                getAppSlug: () => null,
                getBaseUrl: () => window.location.origin
              };
            })();
            """;
    }

    private static void PrintStartupSummary(LocalAppContext context, LocalAppDevServerOptions options, string previewUrl)
    {
        AnsiConsole.MarkupLine("[bold]Automaton local app runtime[/]");
        AnsiConsole.MarkupLine($"App: [cyan]{Markup.Escape(context.DisplayName)}[/]");
        AnsiConsole.MarkupLine($"Mode: [cyan]{Markup.Escape(options.Mode)}[/]");
        AnsiConsole.MarkupLine($"Preview: [link]{previewUrl}[/]");
        AnsiConsole.MarkupLine($"API bridge: [link]{previewUrl}/__automaton[/]");
        AnsiConsole.MarkupLine($"Controllers: {(Directory.Exists(context.BackendPath) ? "[green]enabled[/]" : "[yellow]none[/]")}");
        AnsiConsole.MarkupLine($"Mocks: [cyan]{Markup.Escape(options.Mode)}[/]");
        AnsiConsole.MarkupLine($"Database: [grey]{Markup.Escape(Path.GetRelativePath(context.RootPath, context.DataPath))}[/]");
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop.[/]");
    }

    private static void LogExecution(LocalAppExecutionResult result, bool verbose)
    {
        var statusMarkup = result.Success ? "[green]OK[/]" : "[red]ERROR[/]";
        var reason = string.IsNullOrWhiteSpace(result.FailureReason) ? string.Empty : $" ({result.FailureReason})";
        AnsiConsole.MarkupLine($"{Markup.Escape($"[{DateTimeOffset.Now:HH:mm:ss}]")} {statusMarkup} {Markup.Escape(result.ControllerName)}.{Markup.Escape(result.ActionName)} {result.Duration.TotalMilliseconds:F0}ms [grey]{Markup.Escape(result.Mode)}{Markup.Escape(reason)}[/]");

        if (!result.Success)
        {
            AnsiConsole.MarkupLine($"[red]Reason:[/] {Markup.Escape(result.Error)}");
            if (!string.IsNullOrWhiteSpace(result.Traceback))
            {
                AnsiConsole.MarkupLine(Markup.Escape(result.Traceback));
            }
        }

        if (verbose)
        {
            if (!string.IsNullOrWhiteSpace(result.StdOut))
            {
                AnsiConsole.MarkupLine($"[grey]stdout:[/] {Markup.Escape(result.StdOut)}");
            }

            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                AnsiConsole.MarkupLine($"[grey]stderr:[/] {Markup.Escape(result.StdErr)}");
            }
        }
    }

    private static bool ShouldTriggerReload(LocalAppContext context, string changedPath)
    {
        var relativePath = Path.GetRelativePath(context.FrontendPath, changedPath).Replace('\\', '/');
        if (relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        return !BlockedPathPrefixes.Any(prefix => relativePath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) || string.Equals(relativePath, prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static FileSystemWatcher? CreateWatcher(string path, Action<string> onChange)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }

        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName | NotifyFilters.CreationTime
        };

        void Handle(object _, FileSystemEventArgs args) => onChange(args.FullPath);
        void HandleRename(object _, RenamedEventArgs args) => onChange(args.FullPath);

        watcher.Changed += Handle;
        watcher.Created += Handle;
        watcher.Deleted += Handle;
        watcher.Renamed += HandleRename;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private static bool IsBlockedPath(string normalizedPath)
    {
        return BlockedPathPrefixes.Any(prefix =>
            normalizedPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRequestPath(string? requestPath)
    {
        return (requestPath ?? string.Empty)
            .Trim()
            .TrimStart('/')
            .Replace('\\', '/');
    }

    private static string ResolveContentType(string filePath)
    {
        return ContentTypeProvider.TryGetContentType(filePath, out var contentType)
            ? contentType
            : "application/octet-stream";
    }

    private static int FindAvailablePort(int startPort, int attempts)
    {
        for (var offset = 0; offset < attempts; offset++)
        {
            var port = startPort + offset;
            if (IsPortAvailable(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException("Could not find an available local port for the app dev server.");
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                System.Diagnostics.Process.Start("open", url);
            }
            else
            {
                System.Diagnostics.Process.Start("xdg-open", url);
            }
        }
        catch
        {
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] Failed to open browser automatically. Open {Markup.Escape(url)} manually.");
        }
    }

    private sealed record FrontendResponse(int StatusCode, string? ContentType, byte[]? Bytes)
    {
        public static FrontendResponse NotFound() => new(404, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("Not found."));
    }

    private sealed class ReloadNotifier
    {
        private TaskCompletionSource<bool> _nextChange = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForChangeAsync(CancellationToken cancellationToken)
        {
            return _nextChange.Task.WaitAsync(cancellationToken);
        }

        public void Notify()
        {
            var current = Interlocked.Exchange(ref _nextChange, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            current.TrySetResult(true);
        }
    }
}
