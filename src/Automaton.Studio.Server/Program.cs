using Automaton.Core.Scripting;
using Automaton.Studio.Server.Areas.Identity;
using Automaton.Studio.Server.Authentication;
using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Extensions;
using Automaton.Studio.Server.Hubs;
using Automaton.Studio.Server.Middleware;
using Automaton.Studio.Server.Services;
using Common.Authentication;
using Destructurama;
using FluentValidation.AspNetCore;
using Hangfire;
using Hangfire.MySql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.MariaDB.Extensions;
using System.Reflection;
using System.Transactions;
using Automaton.Studio.Server.Services.Interfaces;
using Automaton.Studio.Server.Enums;
using Automaton.Mcp.Client.Extensions;
using System.Text.Json.Serialization;

var applicationBuilder = WebApplication.CreateBuilder(args);
var configurationManager = applicationBuilder.Configuration;

// Use the WebApplicationBuilder's built configuration so environment-specific files
// (e.g. appsettings.Development.json) and user secrets are included automatically.
var configurationService = new ConfigurationService(applicationBuilder.Configuration);

var services = applicationBuilder.Services;

services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        configurationManager.GetConnectionString("MySqlConnection"),
        new MySqlServerVersion(new Version(8, 0, 36))
    )
);

services.AddDatabaseDeveloperPageExceptionFilter();

// Register AI Providers Configuration
services.Configure<AiProvidersConfig>(applicationBuilder.Configuration.GetSection("AiProviders"));

services.Configure<WebAppConfig>(applicationBuilder.Configuration.GetSection("WebAppConfig"));
services.Configure<AppsTunnelConfig>(applicationBuilder.Configuration.GetSection("AppsTunnel"));
services.AddSingleton<ISystemAiRateLimitService, SystemAiRateLimitService>();
services.AddScoped<ISystemAiService, SystemAiService>();
services.AddScoped<IPromptClassifierService, PromptClassifierService>();
services.AddScoped<IAppExecutionLogService, AppExecutionLogService>();

services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Password.RequireDigit = configurationService.RequireDigit;
    options.Password.RequireLowercase = configurationService.RequireLowercase;
    options.Password.RequireUppercase = configurationService.RequireUppercase;
    options.Password.RequireNonAlphanumeric = configurationService.RequireNonAlphanumeric;
    options.Password.RequiredLength = configurationService.RequiredLength;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

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
        });

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
        policy.Requirements.Add(new Automaton.Studio.Server.Authorization.ProjectAdminRequirement()));

    options.AddPolicy("ProjectResourceManager", policy =>
        policy.Requirements.Add(new Automaton.Studio.Server.Authorization.ProjectResourceManagerRequirement()));

    options.AddPolicy("ProjectAppExecutor", policy =>
        policy.Requirements.Add(new Automaton.Studio.Server.Authorization.ProjectAppExecutorRequirement()));

    options.AddPolicy("ProjectMember", policy =>
        policy.Requirements.Add(new Automaton.Studio.Server.Authorization.ProjectMemberRequirement(
            Automaton.Studio.Server.Enums.ProjectRole.ProjectAdmin,
            Automaton.Studio.Server.Enums.ProjectRole.Developer,
            Automaton.Studio.Server.Enums.ProjectRole.AppUser,
            Automaton.Studio.Server.Enums.ProjectRole.Viewer)));
});

services.AddHttpClient();

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

    // Database sink (same for both environments)
    logConfig.WriteTo.MariaDB(
        connectionString: configurationManager.GetConnectionString("MySqlConnection"),
        tableName: "Logs",
        autoCreateTable: false,
        useBulkInsert: true,
        restrictedToMinimumLevel: LogEventLevel.Information,
        batchPostingLimit: 1000,
        period: TimeSpan.FromSeconds(30),
        options: new Serilog.Sinks.MariaDB.MariaDBSinkOptions
        {
            PropertiesToColumnsMapping = new Dictionary<string, string>
            {
                { "Exception", "Exception" },
                { "Level", "Level" },
                { "Message", "Message" },
                { "MessageTemplate", "MessageTemplate" },
                { "Properties", "Properties" },
                { "Timestamp", "Timestamp" },
                { "EventType", "EventType" },
                { "UserName", "UserName" }
            },
            TimestampInUtc = true,
            ExcludePropertiesWithDedicatedColumn = false,
            EnumsAsInts = false,
            LogRecordsCleanupFrequency = TimeSpan.FromHours(2),
            LogRecordsExpiration = TimeSpan.FromDays(31)
        });
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
services.AddScoped<PublicAppsService>();
services.AddScoped<AppStepsService>();
services.AddScoped<AppsStepExecutionService>();
services.AddSingleton<TunnelRequestRegistry>();
services.AddSingleton<FileUploadRegistry>();
services.AddSingleton<FileDownloadRegistry>();
services.AddScoped<AppsControllerExecutionService>();
services.AddScoped(typeof(UserImpersonationServiceFactory<>));
services.AddScoped<FlowsService>();
services.AddScoped<RunnerService>();
services.AddScoped<FlowExecutionHistoryService>();
services.AddScoped<FlowExecutionLogService>();
services.AddScoped<RecentActivityService>();
services.AddScoped<DashboardStatsService>();
services.AddScoped<UserContextService>();
services.AddScoped<AppFileStorageService>();
services.AddSingleton<AppPreviewStorageService>();
services.AddScoped<AppVersionService>();

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
services.AddTransient<RoleManagerService>();
services.AddTransient<ScheduleService>();

services.AddScoped<ApiKeyService>();
services.AddScoped<AiApiKeyService>();
services.AddScoped<AiProviderMetadataService>();

// System Prompt Builders for AI Chat
services.AddScoped<Automaton.Mcp.SystemPrompts.IStepSystemPromptBuilder, Automaton.Mcp.SystemPrompts.StepSystemPromptBuilder>();
services.AddScoped<Automaton.Mcp.SystemPrompts.IAppSystemPromptBuilder, Automaton.Mcp.SystemPrompts.AppSystemPromptBuilder>();

// MCP Tool Providers for AI Chat
services.AddScoped<Automaton.Studio.Server.Services.Mcp.StepMcpToolProvider>();
services.AddScoped<Automaton.Studio.Server.Services.Mcp.AppMcpToolProvider>();
// File IO tool group — wired to app-scoped file system
services.AddScoped<Automaton.Mcp.Tools.IScopedFileSystemFactory,
                   Automaton.Studio.Server.Services.Mcp.Apps.AppScopedFileSystemFactory>();
services.AddScoped<Automaton.Mcp.Tools.IMcpTool,
                   Automaton.Mcp.Tools.FileIoTool>();
// Assessment tool — assess_task (mandatory first call in agent mode)
services.AddScoped<Automaton.Mcp.Tools.IMcpTool,
                   Automaton.Mcp.Tools.AssessmentTool>();
// Planning tool group — propose_plan
services.AddScoped<Automaton.Mcp.Tools.IMcpTool,
                   Automaton.Mcp.Tools.PlanningTool>();
// Instructions tool group — read_instruction, write_instruction, list_instructions
services.AddScoped<Automaton.Mcp.Tools.IMcpTool,
                   Automaton.Mcp.Tools.InstructionsTool>();
// Instruction injector — injects .instructions/ files into system prompts
services.AddScoped<Automaton.Studio.Server.Services.AppInstructionInjector>();
services.AddScoped<Automaton.Studio.Server.Services.AppMemoryService>();

// MCP Client
services.AddAutomatonMcpClient();

// AI Chat History Service
services.AddScoped<Automaton.Studio.Server.Services.Interfaces.IAiChatHistoryService, Automaton.Studio.Server.Services.AiChatHistoryService>();
services.AddScoped<Automaton.Studio.Server.Services.Interfaces.ITokenBudgetService, Automaton.Studio.Server.Services.TokenBudgetService>();
services.AddScoped<Automaton.Studio.Server.Services.Interfaces.ISubscriptionGatewayService, Automaton.Studio.Server.Services.SubscriptionGatewayService>();
services.Configure<Automaton.Studio.Server.Services.StripeOptions>(applicationBuilder.Configuration.GetSection("Stripe"));
services.AddScoped<Automaton.Studio.Server.Services.StripeService>();

// Project/Organization services
services.AddScoped<ProjectService>();
services.AddScoped<ProjectMemberService>();
services.AddScoped<ProjectResourceService>();
services.AddScoped<InvitationTokenService>();
services.AddScoped<ProjectAuditService>();
services.AddScoped<ResourceAuthorizationService>();
services.AddScoped<SensitiveVariableService>();
services.AddScoped<UserSettingsService>();
services.AddScoped<Automaton.Studio.Server.Services.Interfaces.IOrganizationService, Automaton.Studio.Server.Services.OrganizationService>();

// Project authorization handlers
services.AddScoped<IAuthorizationHandler, Automaton.Studio.Server.Authorization.ProjectMemberHandler>();
services.AddScoped<IAuthorizationHandler, Automaton.Studio.Server.Authorization.ProjectAdminHandler>();
services.AddScoped<IAuthorizationHandler, Automaton.Studio.Server.Authorization.ProjectResourceManagerHandler>();
services.AddScoped<IAuthorizationHandler, Automaton.Studio.Server.Authorization.ProjectAppExecutorHandler>();

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

app.ApplyMigrations();

app.UseSerilogRequestLogging(
    options =>
    {
        options.MessageTemplate = "{ClientIP} {RequestScheme} {RequestHost} {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress);
            // TODO! Need to send UserAgent from Blazor application with each HttpClient request
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].FirstOrDefault());
        };
    });

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

// Enable CORS before authentication (only if configured)
if (corsOrigins != null && corsOrigins.Length > 0)
{
    app.UseCors("CorsPolicy");
}

// Plan limit (402) middleware — converts PlanLimitException to structured HTTP 402
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Automaton.Studio.Server.Exceptions.PlanLimitException ex)
    {
        context.Response.StatusCode  = 402;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            code         = "PLAN_LIMIT",
            feature      = ex.FeatureName,
            requiredPlan = ex.RequiredPlan,
            message      = ex.Message
        });
    }
});

app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();

app.MapBlazorHub();
app.MapHub<AutomatonHub>("/api/workflow/hub");
app.MapFallbackToPage("/_Host");

app.Run();
