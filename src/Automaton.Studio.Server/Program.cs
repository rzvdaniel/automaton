using Automaton.Core.Scripting;
using Automaton.Studio.Server.Areas.Identity;
using Automaton.Studio.Server.Authentication;
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
using Automaton.Studio.Server.Services.AiProviders;
using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Services.AiCodeGeneration;

var applicationBuilder = WebApplication.CreateBuilder(args);
var configurationManager = applicationBuilder.Configuration;

var configurationBuilder = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddUserSecrets<Program>(optional: true, reloadOnChange: true)
    .Build();

var configurationService = new ConfigurationService(configurationBuilder);

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
});

services.AddHttpClient();

services.AddRazorPages();
services.AddServerSideBlazor();
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
});

services.AddTransient<UserNameEnricher>();
services.AddHttpContextAccessor();

Serilog.Debugging.SelfLog.Enable(Console.Out);

applicationBuilder.Host.UseSerilog((context, services, config) =>
    config.Destructure.UsingAttributes()
          .Destructure.JsonNetTypes()
          .Enrich.With<EventTypeEnricher>()
          .Enrich.With(services.GetService<UserNameEnricher>())
          .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
          .WriteTo.MariaDB(
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
              })
);

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
services.AddScoped(typeof(UserImpersonationServiceFactory<>));
services.AddScoped<FlowsService>();
services.AddScoped<RunnerService>();
services.AddScoped<FlowExecutionHistoryService>();
services.AddScoped<FlowExecutionLogService>();
services.AddScoped<RecentActivityService>();
services.AddScoped<DashboardStatsService>();
services.AddScoped<UserContextService>();
services.AddScoped<EmailService>();
services.AddScoped<InstallService>();
services.AddScoped<SettingsService>();
services.AddTransient<UsersService>();
services.AddTransient<RoleManagerService>();
services.AddTransient<ScheduleService>();

services.AddScoped<ApiKeyService>();
services.AddScoped<AiApiKeyService>();
services.AddScoped<AiStepGenerationService>();

services.AddScoped<OpenAiProvider>();
services.AddScoped<AnthropicProvider>();
services.AddScoped<GoogleProvider>();
services.AddScoped<AiProviderFactory>();

services.AddScoped(service => configurationService);

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
services.AddEndpointsApiExplorer();
services.AddSwaggerGen();

services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly()));

services.AddFluentValidation(fv => fv.RegisterValidatorsFromAssemblyContaining<Program>());

services.AddScripting();
services.AddAutomatonSteps();
services.AddAutomatonCore();

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
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Load CORS origins from configuration
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
            .AllowAnyHeader();
    }));

    app.UseCors("CorsPolicy"); // ✅ Enable CORS before authentication
}

app.UseAuthentication();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();

app.MapBlazorHub();
app.MapHub<AutomatonHub>("/api/workflow/hub");
app.MapFallbackToPage("/_Host");

app.Run();
