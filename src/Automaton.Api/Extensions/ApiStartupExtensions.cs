using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Hubs;
using Automaton.Api.Logging;
using Automaton.Api.Middleware;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Serilog;
using Serilog.Events;

namespace Automaton.Api.Extensions;

internal static class ApiApplicationInitializationExtensions
{
    internal static async Task InitializeAutomatonApiAsync(this WebApplication app)
    {
        await app.ApplyMigrationsAsync();
        await app.Services.SeedLocalRunnerHostAsync();
        await InfrastructurePlatformAppInitializer.EnsureExistsAsync(app.Services);
    }
}

internal static class InfrastructurePlatformAppInitializer
{
    internal static async Task EnsureExistsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (await dbContext.WebApps.AnyAsync(app => app.Id == InfrastructureAppTrust.PlatformAppId))
        {
            return;
        }

        dbContext.WebApps.Add(new WebApp
        {
            Id = InfrastructureAppTrust.PlatformAppId,
            Name = InfrastructureAppTrust.PlatformAppName,
            Summary = "Platform-owned infrastructure Action modules.",
            Description = "Internal Automaton infrastructure operations.",
            Definition = "{}",
            IsPublished = false,
            IsForkable = false,
            RequiresAuthentication = true,
            Created = DateTime.UtcNow,
            Updated = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }
}

internal static class ApiApplicationPipelineExtensions
{
    internal static void UseAutomatonApiPipeline(this WebApplication app, string[]? corsOrigins, bool logSuccessfulRequests)
    {
        app.UseForwardedHeaders();
        app.UseRouting();
        ApiRequestLoggingMiddleware.Configure(app, logSuccessfulRequests);
        app.UseRateLimiter();
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        ApiEnvironmentMiddleware.Configure(app);
        ApiStaticFileMiddleware.Configure(app);
        if (corsOrigins is { Length: > 0 })
        {
            app.UseCors("CorsPolicy");
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<MultipartUploadAdmissionMiddleware>();
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapControllers();
        app.MapBlazorHub();
        app.MapHub<AutomatonHub>("/api/workflow/hub");
        app.MapFallbackToPage("/_Host");
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

    private static LogEventLevel GetLevel(
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

        if (context.Response.StatusCode >= 400 || elapsed > 1000)
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
