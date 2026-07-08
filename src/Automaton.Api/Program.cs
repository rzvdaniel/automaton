using Automaton.Core.Scripting;
using Automaton.Ai.Extensions;
using Automaton.AiGuidance;
using Automaton.Mcp.Client.Extensions;
using Destructurama;
using FluentValidation.AspNetCore;
using Hangfire;
using Hangfire.MySql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.FileProviders;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Transactions;
using Automaton.Api.Services;
using Automaton.Api.Authentication;
using Automaton.Api.Enums;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Extensions;
using Automaton.Api.Entities;
using Automaton.Api.Authentication.GitHub;
using Automaton.Api.Logging;
using Automaton.Api.Areas.Identity;
using Automaton.Api.Data;
using Automaton.Api.Services.Mcp;
using Automaton.Api.Services.Mcp.Apps;
using Automaton.Api.Security;
using Automaton.Api.Config;
using Automaton.Api.Authorization;
using Automaton.Api.Middleware;
using Automaton.Api.Hubs;
using Automaton.Api.Messaging;
using Automaton.Messaging.Abstractions;
using Automaton.Identity;
using Automaton.Infrastructure;
using DotNetCore.CAP;

var applicationBuilder = WebApplication.CreateBuilder(args);
var configurationManager = applicationBuilder.Configuration;

// Use the WebApplicationBuilder's built configuration so environment-specific files
// (e.g. appsettings.Development.json) and user secrets are included automatically.
var configurationService = new ConfigurationService(applicationBuilder.Configuration);

var services = applicationBuilder.Services;

services.AddAutomatonInfrastructure(configurationManager);

services.AddDatabaseDeveloperPageExceptionFilter();

// Register AI Providers Configuration
services.Configure<AiProvidersConfig>(applicationBuilder.Configuration.GetSection("AiProviders"));
services.AddSingleton<IAiProvidersSettingsService, AiProvidersSettingsService>();

services.Configure<WebAppConfig>(applicationBuilder.Configuration.GetSection("WebAppConfig"));
services.Configure<PublishedAppsConfig>(applicationBuilder.Configuration.GetSection("PublishedApps"));
services.Configure<MediaStorageOptions>(applicationBuilder.Configuration.GetSection(MediaStorageOptions.SectionName));
services.Configure<GitHubOAuthOptions>(applicationBuilder.Configuration.GetSection("GitHub"));
services.Configure<AppsTunnelConfig>(applicationBuilder.Configuration.GetSection("AppsTunnel"));
services.Configure<HostedRunnerOptions>(applicationBuilder.Configuration.GetSection("HostedRunners"));
services.Configure<RabbitMqOptions>(applicationBuilder.Configuration.GetSection("RabbitMq"));
services.Configure<CloudflareTurnstileOptions>(applicationBuilder.Configuration.GetSection(CloudflareTurnstileOptions.SectionName));
services.Configure<AppFileRuleBaselineOptions>(applicationBuilder.Configuration.GetSection("AppFileRules:Baseline"));
services.AddSingleton<ISystemAiRateLimitService, SystemAiRateLimitService>();
services.AddScoped<ISystemAiService, SystemAiService>();
services.AddScoped<IPromptClassifierService, PromptClassifierService>();
services.AddScoped<IApplicationEventService, ApplicationEventService>();
services.AddSingleton<ApiInstanceIdentityService>();

services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Password.RequireDigit = configurationService.RequireDigit;
    options.Password.RequireLowercase = configurationService.RequireLowercase;
    options.Password.RequireUppercase = configurationService.RequireUppercase;
    options.Password.RequireNonAlphanumeric = configurationService.RequireNonAlphanumeric;
    options.Password.RequiredLength = configurationService.RequiredLength;
    options.Lockout.AllowedForNewUsers = configurationService.LockoutAllowedForNewUsers;
    options.Lockout.MaxFailedAccessAttempts = configurationService.MaxFailedAccessAttempts;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(configurationService.DefaultLockoutTimeSpanMinutes);
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

services.AddAutomatonIdentity();

// Configure cookie authentication to not redirect API requests
services.ConfigureApplicationCookie(options =>
{
    options.Events.OnRedirectToLogin = context =>
    {
        // For API requests (including SSE), return 401 instead of redirecting
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        // For API requests, return 403 instead of redirecting
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

services.AddMvc();

services.AddAccessTokenValidator();
services.AddJwtAuthentication(applicationBuilder.Configuration);

services.AddAuthentication()
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationSchemeOptions.DefaultScheme, 
        options => 
        {
            options.HeaderName = "X-API-Key";
            options.QueryParameterName = "api_key";
        })
    .AddGitHubOAuth(applicationBuilder.Configuration);

// Define specific authorization policies for different access levels
services.AddAuthorization(options =>
{
    // Default policy for web users (Blazor UI) - only Identity and JWT
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes("Identity.Application", JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();

    // Policy for API endpoints that should support API keys (runner operations)
    // This policy accepts EITHER API Key OR JWT/Identity authentication
    options.AddPolicy("AllowApiKey", policy =>
        policy.AddAuthenticationSchemes(ApiKeyAuthenticationSchemeOptions.DefaultScheme, JwtBearerDefaults.AuthenticationScheme, "Identity.Application")
    .RequireAuthenticatedUser());

    // Policy for anonymous endpoints (validation, etc.)
    options.AddPolicy("AllowAnonymous", policy =>
        policy.RequireAssertion(_ => true));

    // Admin policy for users with Admin or SysAdmin roles
    options.AddPolicy("Administrators", policy =>
        policy.RequireRole(UserRole.Administrator, UserRole.SystemAdministrator));

    // Project-based authorization policies
    options.AddPolicy("ProjectAdmin", policy =>
        policy.Requirements.Add(new ProjectAdminRequirement()));

    options.AddPolicy("ProjectResourceManager", policy =>
        policy.Requirements.Add(new ProjectResourceManagerRequirement()));

    options.AddPolicy("ProjectAppExecutor", policy =>
        policy.Requirements.Add(new ProjectAppExecutorRequirement()));

    options.AddPolicy("ProjectMember", policy =>
        policy.Requirements.Add(new ProjectMemberRequirement(
            ProjectRole.ProjectAdmin,
            ProjectRole.Developer,
            ProjectRole.AppUser,
            ProjectRole.Viewer)));
});

services.AddHttpClient();
services.AddHttpClient<ICloudflareTurnstileService, CloudflareTurnstileService>();
services.AddSingleton<IEventPublisher, CapEventPublisher>();
services.AddScoped<ITransactionalEventExecutor, CapTransactionalEventExecutor>();
services.AddCap(options =>
{
    options.UseEntityFramework<ApplicationDbContext>();
    var rabbitMqHost = configurationManager["RabbitMq:Host"]
        ?? throw new InvalidOperationException("RabbitMq:Host is required.");
    var rabbitMqUsername = configurationManager["RabbitMq:Username"]
        ?? throw new InvalidOperationException("RabbitMq:Username is required.");
    var rabbitMqPassword = configurationManager["RabbitMq:Password"]
        ?? throw new InvalidOperationException("RabbitMq:Password is required.");

    options.UseRabbitMQ(rabbitMq =>
    {
        rabbitMq.HostName = rabbitMqHost;
        rabbitMq.UserName = rabbitMqUsername;
        rabbitMq.Password = rabbitMqPassword;
        rabbitMq.VirtualHost = configurationManager["RabbitMq:VirtualHost"] ?? "/";
    });
});
services.AddAutomatonRateLimiting();
services.AddSingleton<RefreshTokenCookieService>();

services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    // Cloudflare Tunnel and local reverse proxies do not use stable local proxy IPs here.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

services.AddRazorPages();
services.AddServerSideBlazor();

// Configure SignalR with extended timeouts for long-running script execution
services.Configure<SignalRConfig>(applicationBuilder.Configuration.GetSection("SignalR"));
var signalRConfig = applicationBuilder.Configuration.GetSection("SignalR").Get<SignalRConfig>() ?? new SignalRConfig();
services.AddSignalR(options =>
{
    // Client timeout - how long the server waits before considering a client disconnected
    // Default is 30 seconds, increased for long-running Python scripts with HTTP calls
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(signalRConfig.ClientTimeoutIntervalMinutes);
    
    // Keep-alive interval - how often the server sends pings to clients
    options.KeepAliveInterval = TimeSpan.FromSeconds(signalRConfig.KeepAliveIntervalSeconds);
    
    // Handshake timeout - how long to wait for initial handshake
    options.HandshakeTimeout = TimeSpan.FromSeconds(signalRConfig.HandshakeTimeoutSeconds);
    
    // App controller responses are small JSON payloads — files are now uploaded by the runner directly
    // to /api/runner/files and the browser downloads them via GET /api/apps/files/{token}, so large
    // binary content never travels through SignalR. 1 MB is generous for any JSON response.
    options.MaximumReceiveMessageSize = signalRConfig.MaximumReceiveMessageSizeBytes;
    
    // Enable detailed errors in development for debugging
    options.EnableDetailedErrors = applicationBuilder.Environment.IsDevelopment();
});

services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider<IdentityUser>>();
services.AddControllers(options =>
{
    // Apply default authorization to all controllers
    var policy = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes("Identity.Application", JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
    options.Filters.Add<GlobalExceptionFilter>();
})
.AddJsonOptions(options =>
{
    // Serialize enums as strings instead of integers
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

services.AddTransient<UserNameEnricher>();
services.AddHttpContextAccessor();

Serilog.Debugging.SelfLog.Enable(Console.Out);


applicationBuilder.Host.UseSerilog((context, services, config) =>
{
    var logDirectory = context.Configuration["Logging:File:Path"] ?? "/app/logs";
    var retainedFileCountLimit = context.Configuration.GetValue<int?>("Logging:File:RetainedFileCountLimit") ?? 14;
    var logConfig = config
        .Destructure.UsingAttributes()
        .Destructure.JsonNetTypes()
        .Enrich.With<EventTypeEnricher>()
        .Enrich.With(services.GetService<UserNameEnricher>())
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning);

    // Console logging - environment-specific
    if (applicationBuilder.Environment.IsDevelopment())
    {
        // Development: detailed output for debugging
        logConfig.WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}",
            restrictedToMinimumLevel: LogEventLevel.Debug);
    }
    else
    {
        // Production: compact, secure output
        logConfig.WriteTo.Console(
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
            restrictedToMinimumLevel: LogEventLevel.Information);
    }

    logConfig.WriteTo.File(
        path: Path.Combine(logDirectory, "diagnostic-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: retainedFileCountLimit,
        restrictedToMinimumLevel: LogEventLevel.Information,
        shared: true,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}");
});

services.AddHangfire(x =>
{
    object handler = configurationService.DatabaseType switch
    {
        ConfigurationService.MySqlDatabaseType => 
            x.UseStorage(new MySqlStorage(configurationManager.GetConnectionString("MySqlConnection"),
                new MySqlStorageOptions
                {
                    TransactionIsolationLevel = IsolationLevel.ReadCommitted,
                    QueuePollInterval = TimeSpan.FromSeconds(15),
                    JobExpirationCheckInterval = TimeSpan.FromHours(1),
                    CountersAggregateInterval = TimeSpan.FromMinutes(5),
                    PrepareSchemaIfNecessary = true,
                    DashboardJobListLimit = 50000,
                    TransactionTimeout = TimeSpan.FromMinutes(1),
                    TablesPrefix = "hangfire."
                })),

        _ => throw new Exception($"Unsupported hangfire provider: {configurationService.DatabaseType}")
    };
});  

services.AddHangfireServer();

services.AddScoped<CustomStepsService>();
services.AddScoped<AppsService>();
services.AddScoped<AdminAppsService>();
services.AddScoped<PublicAppsService>();
services.AddScoped<WebAppStarService>();
services.AddScoped<AppStepsService>();
services.AddScoped<AppsStepExecutionService>();
services.AddSingleton<TunnelRequestRegistry>();
services.AddSingleton<FileUploadRegistry>();
services.AddSingleton<FileDownloadRegistry>();
services.AddScoped<AppsControllerExecutionService>();
services.AddScoped<IAppDeployCapabilityService, AppDeployCapabilityService>();
services.AddScoped(typeof(UserImpersonationServiceFactory<>));
services.AddScoped<FlowsService>();
services.AddScoped<RunnerService>();
services.AddScoped<AdminRunnersService>();
services.AddScoped<FlowExecutionHistoryService>();
services.AddScoped<FlowExecutionLogService>();
services.AddScoped<RecentActivityService>();
services.AddScoped<AccountRegistrationService>();
services.AddScoped<IAuditEventService, AuditEventService>();
services.AddScoped<AuditEventService>();
services.AddScoped<DashboardStatsService>();
services.AddScoped<UserContextService>();
services.AddScoped<AdminAuthorizationService>();
services.AddScoped<AppFileStorageService>();
services.AddScoped<AppMediaStorageService>();
services.AddScoped<PublishedAppStorageService>();
services.AddSingleton<StaticRuntimeExportService>();
services.AddScoped<AppArchiveService>();
services.AddScoped<AppVersionService>();
services.AddSingleton<PrivateAppLaunchService>();
services.AddSingleton<RuntimeCookieService>();

// Register email service implementation based on environment and configuration.
var useNoOpEmail = applicationBuilder.Environment.IsDevelopment() && !configurationService.EmailConfig.SendEmailsInDevelopment;
if (useNoOpEmail)
{
    services.AddScoped<NoOpEmailService>();
    services.AddScoped<IEmailService>(sp => sp.GetRequiredService<NoOpEmailService>());
}
else
{
    services.AddScoped<EmailService>();
    services.AddScoped<IEmailService>(sp => sp.GetRequiredService<EmailService>());
}

services.AddScoped<InstallService>();
services.AddScoped<SettingsService>();
services.AddTransient<UsersService>();
services.AddScoped<ConfirmedEmailGuardService>();
services.AddScoped<AuthTokenService>();
services.AddTransient<RoleManagerService>();
services.AddTransient<ScheduleService>();

services.AddScoped<ApiKeyService>();
services.AddScoped<AiApiKeyService>();
services.AddScoped<AiProviderMetadataService>();
services.AddScoped<IAiGuidanceRuntimeService, AiGuidanceRuntimeService>();
services.AddScoped<IAutomatonGuidanceRuntimeProvider, AiGuidanceRuntimeService>();
services.AddScoped<AdminAiGuidanceService>();
services.AddScoped<AdminDocumentationService>();
services.AddScoped<IHostedRunnerRemoteCommandExecutor, SshHostedRunnerRemoteCommandExecutor>();
services.AddScoped<HostedRunnerProvisioningService>();
services.AddScoped<HostedRunnerManagementService>();

// System Prompt Builders for AI Chat
services.AddScoped<Automaton.Mcp.SystemPrompts.IStepSystemPromptBuilder, Automaton.Mcp.SystemPrompts.StepSystemPromptBuilder>();
services.AddScoped<Automaton.Mcp.SystemPrompts.IAppVerificationPromptStrategy, Automaton.Mcp.SystemPrompts.ContinuousVerificationPromptStrategy>();
services.AddScoped<Automaton.Mcp.SystemPrompts.IAppVerificationPromptStrategy, Automaton.Mcp.SystemPrompts.BalancedVerificationPromptStrategy>();
services.AddScoped<Automaton.Mcp.SystemPrompts.IAppVerificationPromptStrategy, Automaton.Mcp.SystemPrompts.FinalGateVerificationPromptStrategy>();
services.AddScoped<Automaton.Mcp.SystemPrompts.IAppExecutionPolicyPromptStrategy, Automaton.Mcp.SystemPrompts.DefaultExecutionPolicyPromptStrategy>();
services.AddScoped<Automaton.Mcp.SystemPrompts.IAppExecutionPolicyPromptStrategy, Automaton.Mcp.SystemPrompts.LeanToolsExecutionPolicyPromptStrategy>();
services.AddSingleton<IAutomatonGuidanceProvider, EmbeddedAutomatonGuidanceProvider>();
services.AddSingleton<Automaton.Mcp.SystemPrompts.IAppPromptContextSelector, Automaton.Mcp.SystemPrompts.KeywordAppPromptContextSelector>();
services.AddScoped<Automaton.Mcp.SystemPrompts.IAppSystemPromptBuilder, Automaton.Mcp.SystemPrompts.AppSystemPromptBuilder>();

// MCP Tool Providers for AI Chat
services.AddScoped<StepMcpToolProvider>();
services.AddScoped<AppMcpToolProvider>();
services.AddSingleton<Automaton.Mcp.Tools.IUiAutomationActionAckService,
                     Automaton.Mcp.Tools.UiAutomationActionAckService>();
// File IO tool group — wired to app-scoped file system
services.AddScoped<Automaton.Mcp.Tools.IScopedFileSystemFactory,
                   AppScopedFileSystemFactory>();
services.AddScoped<Automaton.Mcp.Tools.FileIoTool>();
services.AddScoped<Automaton.Mcp.Tools.IMcpTool>(sp => sp.GetRequiredService<Automaton.Mcp.Tools.FileIoTool>());
// Planning tool group — propose_plan
services.AddScoped<Automaton.Mcp.Tools.IMcpTool,
                   Automaton.Mcp.Tools.PlanningTool>();
// Instructions tool group — read_instruction, write_instruction, list_instructions
services.AddScoped<Automaton.Mcp.Tools.IMcpTool,
                   Automaton.Mcp.Tools.InstructionsTool>();
// Embedded Automaton guidance tool group — list_automaton_guidance, get_automaton_guidance
services.AddScoped<Automaton.Mcp.Tools.IMcpTool,
                   Automaton.Mcp.Tools.AutomatonGuidanceTool>();
// UI automation tool group — emits client-action envelopes for preview interactions
services.AddScoped<Automaton.Mcp.Tools.IMcpTool,
                   Automaton.Mcp.Tools.UiAutomationTool>();
// Instruction injector — injects .instructions/ files into system prompts
services.AddScoped<AppInstructionInjector>();
services.AddScoped<AppMemoryService>();

// AI providers + MCP client
services.AddAutomatonAi();
services.AddAutomatonMcp();
// Replace the default IAiProviderRegistry with the dynamic decorator that lazy-loads
// OpenAI-compatible providers from the database on first use — no restart needed.
services.AddSingleton<Automaton.Ai.IAiProviderRegistry, DynamicAiProviderRegistry>();

// AI Chat History Service
services.AddScoped<IAiChatHistoryService, AiChatHistoryService>();
services.AddScoped<ITokenBudgetService, TokenBudgetService>();
services.AddScoped<IAppVerificationStrategyResolver, AppVerificationStrategyResolver>();
services.Configure<StripeOptions>(applicationBuilder.Configuration.GetSection("Stripe"));
services.AddScoped<StripeService>();

// Project/Organization services
services.AddScoped<ProjectService>();
services.AddScoped<ProjectMemberService>();
services.AddScoped<ProjectResourceService>();
services.AddScoped<InvitationTokenService>();
services.AddScoped<ResourceAuthorizationService>();
services.AddScoped<SensitiveVariableService>();
services.AddScoped<UserSettingsService>();
services.AddScoped<IOrganizationService, OrganizationService>();

// Project authorization handlers
services.AddScoped<IAuthorizationHandler, ProjectMemberHandler>();
services.AddScoped<IAuthorizationHandler, ProjectAdminHandler>();
services.AddScoped<IAuthorizationHandler, ProjectResourceManagerHandler>();
services.AddScoped<IAuthorizationHandler, ProjectAppExecutorHandler>();

services.AddScoped(service => configurationService);

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
services.AddEndpointsApiExplorer();
services.AddSwaggerGen(options =>
{
    // Use fully qualified type names for schema IDs to avoid conflicts
    // when multiple types have the same class name in different namespaces
    options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
});

services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly()));

services.AddFluentValidation(fv => fv.RegisterValidatorsFromAssemblyContaining<Program>());

services.AddScripting();
services.AddAutomatonSteps();
services.AddAutomatonCore();

// Load CORS origins from configuration and register if configured
var corsOrigins = configurationManager.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (corsOrigins != null && corsOrigins.Length > 0)
{
    services.AddCors(options => options.AddPolicy("CorsPolicy", builder =>
    {
        // CORS is ONLY for browser-based web apps
        // Runners use API Key authentication (X-API-Key header) - NO CORS needed!
        builder
            .WithOrigins(corsOrigins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials(); // Required for Authorization header in SSE requests
    }));
}

var app = applicationBuilder.Build();
var logSuccessfulRequests = configurationManager.GetValue<bool>("RequestLogging:LogSuccessfulRequests");

await app.ApplyMigrationsAsync();

app.UseForwardedHeaders();

app.UseRouting();

app.UseWhen(
    ctx => !LoggingRequestFilter.ShouldSkipRequestLogging(ctx),
    builder =>
    {
        builder.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "{ClientIP} {RequestScheme} {RequestHost} {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

            options.GetLevel = (httpContext, elapsed, exception) =>
            {
                if (exception != null || httpContext.Response.StatusCode >= 500)
                {
                    return LogEventLevel.Error;
                }

                if (httpContext.Response.StatusCode >= 400 || elapsed > 1000)
                {
                    return LogEventLevel.Warning;
                }

                if (applicationBuilder.Environment.IsDevelopment())
                {
                    return LogEventLevel.Information;
                }

                return logSuccessfulRequests
                    ? LogEventLevel.Information
                    : LogEventLevel.Debug;
            };

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                diagnosticContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress?.ToString());
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.FirstOrDefault());
                diagnosticContext.Set("UserName", httpContext.User.Identity?.Name);
            };
        });
    });

app.UseRateLimiter();

app.UseMiddleware<ExceptionHandlingMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

// Serve user app files as static content (for better performance)
// Files are served from: /app-content/{userId}/{appId}/{file}
// This bypasses controller logic for faster delivery
var appFilesPath = Path.Combine(app.Environment.ContentRootPath, "AppData", "apps");
if (Directory.Exists(appFilesPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(appFilesPath),
        RequestPath = "/app-content",
        OnPrepareResponse = ctx =>
        {
            // Disable caching for preview/draft apps to ensure changes are immediately visible
            // Published apps use UserAppsController routes with appropriate caching
            ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
            ctx.Context.Response.Headers.Expires = "0";

            // Security headers for user-generated content
            ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
            ctx.Context.Response.Headers.XFrameOptions = "SAMEORIGIN";
        }
    });
}

// Serve uploaded marketplace media from the persistent AppData volume.
// Public URLs stay the same even though storage lives outside wwwroot.
var appMediaRootPath = Path.Combine(app.Environment.ContentRootPath, "AppData", "media");
ConfigureStaticMediaPath(app, Path.Combine(appMediaRootPath, "app-thumbnails"), "/app-thumbnails");
ConfigureStaticMediaPath(app, Path.Combine(appMediaRootPath, "app-screenshots"), "/app-screenshots");

// Enable CORS before authentication (only if configured)
if (corsOrigins != null && corsOrigins.Length > 0)
{
    app.UseCors("CorsPolicy");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapControllers();

app.MapBlazorHub();
app.MapHub<AutomatonHub>("/api/workflow/hub");
app.MapFallbackToPage("/_Host");

app.Run();

static void ConfigureStaticMediaPath(WebApplication app, string physicalPath, string requestPath)
{
    if (!Directory.Exists(physicalPath))
    {
        Directory.CreateDirectory(physicalPath);
    }

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(physicalPath),
        RequestPath = requestPath,
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers.CacheControl = "public,max-age=86400";
            ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
        }
    });
}
