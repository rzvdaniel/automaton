using Automaton.Api.Security;
using Automaton.Api.Services;
using Automaton.Api.Authentication;
using Automaton.Api.Data;
using Automaton.Api.Hubs;
using Automaton.Api.Logging;
using Automaton.Api.Middleware;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Events;

namespace Automaton.Api.Extensions;

internal static class ApiApplicationInitializationExtensions
{
    internal static async Task InitializeAutomatonApiAsync(this WebApplication app)
    {
        await app.ApplyMigrationsAsync();
    }
}

internal static class ApiApplicationPipelineExtensions
{
    internal static void UseAutomatonApiPipeline(this WebApplication app, string[]? corsOrigins, bool logSuccessfulRequests)
    {
        app.UseForwardedHeaders();
        app.UseRouting();
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                ResponseSecurityHeaders.Apply(context.Response, corsOrigins ?? []);
                return Task.CompletedTask;
            });
            return next();
        });
        ApiRequestLoggingMiddleware.Configure(app, logSuccessfulRequests);
        app.UseMiddleware<ApiKeyFailureMiddleware>();
        app.UseRateLimiter();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        ApiEnvironmentMiddleware.Configure(app);
        ApiStaticFileMiddleware.Configure(app);
        ApiCorsMiddleware.Configure(app, corsOrigins);

        app.UseWebSockets();
        // Tells an AI connector where to sign in (MCP authorization: protected resource metadata).
        app.UseWhen(context => context.Request.Path.StartsWithSegments("/mcp"), branch => branch.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
                    context.Response.Headers.WWWAuthenticate =
                        $"Bearer resource_metadata=\"{context.Request.Scheme}://{context.Request.Host}/.well-known/oauth-protected-resource/mcp\"";
                return Task.CompletedTask;
            });
            return next(context);
        }));
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<PasswordChangeRequirement>();
        app.UseMiddleware<MultipartUploadAdmissionMiddleware>();
        app.UseWhen(context => context.Request.Path.StartsWithSegments("/mcp"), branch => branch.Use((context, next) =>
        {
            var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = Automaton.Api.Mcp.AppMcpManager.MaxRequestBytes;
            return next(context);
        }));
        app.MapAutomatonApiEndpoints();
    }

    /// <summary>Every route the API serves. ApiRouteAuthorizationTests maps this same method and checks each route's sign-in rule.</summary>
    internal static void MapAutomatonApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/health", () => Results.Json(HealthResponse())).AllowAnonymous();
        app.MapControllers();
        // deploy_app carries up to 50 MB of file text inside JSON; the room above that covers JSON escaping.
        app.MapMcp("/mcp").RequireAuthorization("DeveloperApiAccess");
        // The stateless transport maps no GET, so a GET fell through to the Studio page. Without a key it now gets the
        // same 401 challenge as a POST; with a key, 405 (MCP here is POST only).
        app.MapGet("/mcp", () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed)).RequireAuthorization("DeveloperApiAccess");
        app.MapBlazorHub();
        app.MapHub<AutomatonHub>("/api/workflow/hub");
        app.MapRunnerTunnel();
        app.MapApiNotFound();
        app.MapDotfileNotFound();
        app.MapFallbackToPage("/_Host");
    }

    /// <summary>Any unmatched /api request, for every method, answers 404 with a JSON problem body instead of the Studio page.</summary>
    internal static void MapApiNotFound(this IEndpointRouteBuilder routes) =>
        routes.MapFallback("/api/{**path}", () =>
            Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "No API endpoint matches this request."));

    /// <summary>
    /// Scanner paths such as /.git/config or /.env.bak answer an empty 404 instead of the Studio page. Only unmatched paths
    /// reach this fallback (it ranks just above the Studio page), and /.well-known stays reachable.
    /// </summary>
    internal static void MapDotfileNotFound(this IEndpointRouteBuilder routes) =>
        routes.MapFallback("{**path:regex(^(.*/)?\\.(?!well-known(/|$)).*$)}", () => Results.StatusCode(StatusCodes.Status404NotFound))
            .Add(builder => ((RouteEndpointBuilder)builder).Order = int.MaxValue - 1);

    /// <summary>The anonymous /health body: only the status. The release is for signed-in users (GET /api/install/version).</summary>
    internal static object HealthResponse() => new { status = "ok" };
}

internal static class ApiCorsMiddleware
{
    internal static void Configure(WebApplication app, string[]? corsOrigins)
    {
        if (corsOrigins is not { Length: > 0 })
            return;

        app.UseWhen(
            context => !context.Request.Path.StartsWithSegments("/runtime-tunnel/apps", StringComparison.OrdinalIgnoreCase),
            branch => branch.UseCors("CorsPolicy"));
    }
}

internal static class ApiRequestLoggingMiddleware
{
    internal static void Configure(WebApplication app, bool logSuccessfulRequests)
    {
        app.UseWhen(
            context => !LoggingRequestFilter.ShouldSkipRequestLogging(context),
            branch => branch.UseSerilogRequestLogging(options =>
            {
                options.MessageTemplate =
                    "{ClientIP} {RequestScheme} {RequestHost} {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
                options.GetLevel = (context, elapsed, exception) => GetLevel(
                    context,
                    elapsed,
                    exception,
                    app.Environment.IsDevelopment(),
                    logSuccessfulRequests);
                options.EnrichDiagnosticContext = EnrichDiagnosticContext;
            }));
    }

    internal static LogEventLevel GetLevel(
        HttpContext context,
        double elapsed,
        Exception? exception,
        bool isDevelopment,
        bool logSuccessfulRequests)
    {
        if (exception is not null || context.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        var isUpgrade = context.Response.StatusCode == StatusCodes.Status101SwitchingProtocols;
        var isForwardAuthDenial = context.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
            && context.Request.Path.StartsWithSegments("/internal/runtime/auth", StringComparison.OrdinalIgnoreCase);

        if (isForwardAuthDenial)
        {
            return LogEventLevel.Information;
        }

        if (context.Response.StatusCode >= 400 || (elapsed > 1000 && !isUpgrade))
        {
            return LogEventLevel.Warning;
        }

        if (isDevelopment || logSuccessfulRequests)
        {
            return LogEventLevel.Information;
        }

        return LogEventLevel.Debug;
    }

    private static void EnrichDiagnosticContext(IDiagnosticContext diagnosticContext, HttpContext context)
    {
        diagnosticContext.Set("RequestHost", context.Request.Host.Value);
        diagnosticContext.Set("RequestScheme", context.Request.Scheme);
        diagnosticContext.Set("ClientIP", context.Connection.RemoteIpAddress?.ToString());
        diagnosticContext.Set("UserAgent", context.Request.Headers.UserAgent.FirstOrDefault());
        diagnosticContext.Set("UserName", context.User.Identity?.Name);
    }
}

internal static class ApiEnvironmentMiddleware
{
    internal static void Configure(WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.UseMigrationsEndPoint();
            app.UseSwagger();
            app.UseSwaggerUI();
            return;
        }

        app.UseExceptionHandler("/Error");
        app.UseHsts();
        app.UseHttpsRedirection();
    }
}

internal static class ApiStaticFileMiddleware
{
    internal static void Configure(WebApplication app)
    {
        app.UseStaticFiles();
        ConfigureUserAppFiles(app);

        var mediaRoot = Path.Combine(app.Environment.ContentRootPath, "AppData", "media");
        ConfigureStaticMediaPath(app, Path.Combine(mediaRoot, "app-thumbnails"), "/app-thumbnails");
        ConfigureStaticMediaPath(app, Path.Combine(mediaRoot, "app-screenshots"), "/app-screenshots");
    }

    private static void ConfigureUserAppFiles(WebApplication app)
    {
        var appFilesPath = Path.Combine(app.Environment.ContentRootPath, "AppData", "apps");
        if (!Directory.Exists(appFilesPath))
        {
            return;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(appFilesPath),
            RequestPath = "/app-content",
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                context.Context.Response.Headers.Pragma = "no-cache";
                context.Context.Response.Headers.Expires = "0";
                context.Context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Context.Response.Headers.XFrameOptions = "SAMEORIGIN";
            }
        });
    }

    private static void ConfigureStaticMediaPath(WebApplication app, string physicalPath, string requestPath)
    {
        if (!Directory.Exists(physicalPath))
        {
            Directory.CreateDirectory(physicalPath);
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(physicalPath),
            RequestPath = requestPath,
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.CacheControl = "public,max-age=86400";
                context.Context.Response.Headers.XContentTypeOptions = "nosniff";
            }
        });
    }
}
