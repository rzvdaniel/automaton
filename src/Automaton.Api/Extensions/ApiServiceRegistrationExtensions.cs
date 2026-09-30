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
using Microsoft.AspNetCore.DataProtection;
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
using Automaton.Api.Controllers.Admin;
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
using Automaton.Api.Models;
using Automaton.Api.Authorization;
using Automaton.Api.Middleware;
using Automaton.Api.Hubs;
using Automaton.Api.Messaging;
using Automaton.Messaging.Abstractions;
using Automaton.Identity;
using Automaton.Infrastructure;
using DotNetCore.CAP;

using Microsoft.Extensions.DependencyInjection;
namespace Automaton.Api.Extensions;

internal static class AutomatonApiServiceRegistration
{
    internal static IServiceCollection AddAutomatonApiServices(
        this IServiceCollection services,
        WebApplicationBuilder applicationBuilder,
        string[]? corsOrigins)
    {
        var configurationService = new ConfigurationService(applicationBuilder.Configuration);
        services.AddAutomatonInfrastructure(applicationBuilder.Configuration);
        services.AddApiHostingServices(applicationBuilder, configurationService);
        services.AddApiFeatureServices(applicationBuilder, configurationService, corsOrigins);
        return services;
    }
}

internal static class ApiHostingServiceRegistration
{
    internal static IServiceCollection AddApiHostingServices(
        this IServiceCollection services,
        WebApplicationBuilder applicationBuilder,
        ConfigurationService configurationService)
    {
        services.AddApiCoreOptions(applicationBuilder);
        services.AddApiIdentity(applicationBuilder, configurationService);
        services.AddApiCookieAuthentication();
        services.AddApiAuthentication(applicationBuilder);
        services.AddApiAuthorization();
        services.AddApiMessaging(applicationBuilder);
        services.AddApiForwardedHeaders();
        services.AddApiSignalR(applicationBuilder);
        services.AddApiControllers();
        Serilog.Debugging.SelfLog.Enable(Console.Out);
        services.ConfigureApiLogging(applicationBuilder);
        services.AddApiHangfire(applicationBuilder, configurationService);
        return services;
    }
}

internal static class ApiFeatureServiceRegistration
{
    internal static IServiceCollection AddApiFeatureServices(
        this IServiceCollection services,
        WebApplicationBuilder applicationBuilder,
        ConfigurationService configurationService,
        string[]? corsOrigins)
    {
        services.AddApiAppCatalogServices();
        services.AddApiAppExecutionServices(applicationBuilder);
        services.AddApiRunnerServices(applicationBuilder);
        services.AddApiEmailService(applicationBuilder, configurationService);
        services.AddApiAccountServices();
        services.AddApiOperationsServices();
        services.AddApiAiPromptServices();
        services.AddApiMcpToolServices();
        services.AddApiAiClientServices(applicationBuilder);
        services.AddApiProjectServices();
        services.AddApiFoundationServices(configurationService);
        services.AddApiCors(corsOrigins);
        return services;
    }
}

internal static class ApiCoreOptionRegistration
{
    internal static IServiceCollection AddApiCoreOptions(this IServiceCollection services, WebApplicationBuilder applicationBuilder)
    {
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
        services.Configure<RunnerEnrollmentOptions>(applicationBuilder.Configuration.GetSection(RunnerEnrollmentOptions.SectionName));
        services.Configure<RabbitMqOptions>(applicationBuilder.Configuration.GetSection("RabbitMq"));
        services.Configure<InfrastructureRunnerRabbitMqOptions>(applicationBuilder.Configuration.GetSection(InfrastructureRunnerRabbitMqOptions.SectionName));
        services.Configure<HostedRunnerRabbitMqOptions>(applicationBuilder.Configuration.GetSection(HostedRunnerRabbitMqOptions.SectionName));
        services.Configure<CloudflareTurnstileOptions>(applicationBuilder.Configuration.GetSection(CloudflareTurnstileOptions.SectionName));
        services.Configure<AppFileRuleBaselineOptions>(applicationBuilder.Configuration.GetSection("AppFileRules:Baseline"));
        services.AddSingleton<ISystemAiRateLimitService, SystemAiRateLimitService>();
        services.AddScoped<ISystemAiService, SystemAiService>();
        services.AddScoped<CliAuthorizationService>();
        services.AddScoped<IPromptClassifierService, PromptClassifierService>();
        services.AddScoped<IApplicationEventService, ApplicationEventService>();
        services.AddSingleton<ApiInstanceIdentityService>();
        return services;
    }
}

internal static class ApiIdentityRegistration
{
    internal static IServiceCollection AddApiIdentity(this IServiceCollection services, WebApplicationBuilder applicationBuilder, ConfigurationService configurationService)
    {
        var configurationManager = applicationBuilder.Configuration;
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

        var dataProtectionKeysPath = configurationManager["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
        {
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
                .SetApplicationName("Automaton.Api");
        }

        return services;
    }
}

internal static class ApiCookieRegistration
{
    internal static IServiceCollection AddApiCookieAuthentication(this IServiceCollection services)
    {
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
        return services;
    }
}

internal static class ApiAuthenticationRegistration
{
    internal static IServiceCollection AddApiAuthentication(this IServiceCollection services, WebApplicationBuilder applicationBuilder)
    {
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
            .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationSchemeOptions.InfrastructureRunnerEnrollmentScheme,
                options =>
                {
                    options.HeaderName = "X-API-Key";
                    options.QueryParameterName = string.Empty;
                })
            .AddGitHubOAuth(applicationBuilder.Configuration);

        // Define specific authorization policies for different access levels
        return services;
    }
}

internal static class ApiAuthorizationRegistration
{
    internal static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Default policy for web users (Blazor UI) - only Identity and JW
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes("Identity.Application", JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();

            // Policy for API endpoints that should support API keys (runner operations)
            // This policy accepts EITHER API Key OR JWT/Identity authentication
            options.AddPolicy("AllowApiKey", policy =>
                policy.AddAuthenticationSchemes(ApiKeyAuthenticationSchemeOptions.DefaultScheme, JwtBearerDefaults.AuthenticationScheme, "Identity.Application")
              .RequireAuthenticatedUser()
              .RequireAssertion(context =>
              {
                  var scopeClaim = context.User.FindFirst("api_key_scope")?.Value;
                  return !Enum.TryParse<Automaton.Core.Enums.ApiKeyScope>(scopeClaim, out var scope)
                      || scope != Automaton.Core.Enums.ApiKeyScope.InfrastructureRunnerEnroll;
              }));

            options.AddPolicy(SelfHostedRunnerInstallationAuthorization.PolicyName, policy =>
                policy.AddAuthenticationSchemes(ApiKeyAuthenticationSchemeOptions.DefaultScheme, JwtBearerDefaults.AuthenticationScheme, "Identity.Application")
                    .RequireAuthenticatedUser()
                    .RequireAssertion(context => SelfHostedRunnerInstallationAuthorization.IsAllowed(context.User)));

            // Policy for anonymous endpoints (validation, etc.)
            options.AddPolicy("AllowAnonymous", policy =>
                policy.RequireAssertion(_ => true));

            options.AddPolicy("InfrastructureRunnerEnrollment", policy =>
                policy.AddAuthenticationSchemes(ApiKeyAuthenticationSchemeOptions.InfrastructureRunnerEnrollmentScheme)
                    .RequireAuthenticatedUser()
                    .RequireClaim("api_key_scope", Automaton.Core.Enums.ApiKeyScope.InfrastructureRunnerEnroll.ToString()));

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
        return services;
    }
}

internal static class ApiMessagingRegistration
{
    internal static IServiceCollection AddApiMessaging(this IServiceCollection services, WebApplicationBuilder applicationBuilder)
    {
        var configurationManager = applicationBuilder.Configuration;
        services.AddHttpClient();
        services.AddHttpClient(nameof(HostedRunnerBrokerProvisioner))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient<ICloudflareTurnstileService, CloudflareTurnstileService>();
        services.AddSingleton<IEventPublisher, CapEventPublisher>();
        services.AddTransient<ScheduledActionExecutionRequestedCapSubscriber>();
        services.AddTransient<DeploymentPackageRequestedCapSubscriber>();
        services.AddScoped<InfrastructureActionProvisioningHandler>();
        services.AddScoped<InfrastructureCommandCallbackService>();
        services.AddScoped<InfrastructureCommandResultService>();
        services.AddScoped<HostedRunnerRestartReconciler>();
        services.AddScoped<HostedRunnerLifecycleReconciler>();
        services.AddScoped<TrustedInfrastructureActionDeploymentService>();
        services.AddScoped<ITrustedInfrastructureActionDeploymentService>(sp => sp.GetRequiredService<TrustedInfrastructureActionDeploymentService>());
        services.AddScoped<HostedRunnerStorageActionDeployment>();
        services.AddScoped<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<ISecretStore, SecretStore>();
        services.AddScoped<IExecutionSecretGrantService, ExecutionSecretGrantService>();
        services.AddHostedService<SecretGrantCleanupService>();
        services.AddScoped<ITransactionalEventExecutor, CapTransactionalEventExecutor>();
        services.AddCap(options =>
        {
            // A deployment blocked by another executor must remain retryable until
            // the five-minute execution lease can be observed as expired.
            options.FailedRetryCount = 50;
            options.FailedRetryInterval = 60;
            CapRegistration.ConfigureStorage(options);
            var rabbitMqHost = configurationManager["RabbitMq:Host"]
                ?? throw new InvalidOperationException("RabbitMq:Host is required.");
            var rabbitMqUsername = configurationManager["RabbitMq:Username"]
                ?? throw new InvalidOperationException("RabbitMq:Username is required.");
            var rabbitMqPassword = configurationManager["RabbitMq:Password"]
                ?? throw new InvalidOperationException("RabbitMq:Password is required.");

            options.UseRabbitMQ(rabbitMq =>
            {
                rabbitMq.HostName = rabbitMqHost;
                rabbitMq.Port = int.TryParse(configurationManager["RabbitMq:Port"], out var rabbitMqPort)
                    ? rabbitMqPort
                    : 5672;
                rabbitMq.UserName = rabbitMqUsername;
                rabbitMq.Password = rabbitMqPassword;
                rabbitMq.VirtualHost = configurationManager["RabbitMq:VirtualHost"] ?? "/";
            });
        });
        services.AddAutomatonRateLimiting();
        services.AddSingleton<RefreshTokenCookieService>();
        return services;
    }
}

internal static class ApiForwardedHeaderRegistration
{
    internal static IServiceCollection AddApiForwardedHeaders(this IServiceCollection services)
    {
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

        return services;
    }
}

internal static class ApiSignalRRegistration
{
    internal static IServiceCollection AddApiSignalR(this IServiceCollection services, WebApplicationBuilder applicationBuilder)
    {
        services.AddRazorPages();
        services.AddServerSideBlazor();
        services.Configure<SignalRConfig>(applicationBuilder.Configuration.GetSection("SignalR"));
        var signalRConfig = applicationBuilder.Configuration.GetSection("SignalR").Get<SignalRConfig>() ?? new SignalRConfig();
        services.AddSignalR(options =>
        {
            options.ClientTimeoutInterval = TimeSpan.FromMinutes(signalRConfig.ClientTimeoutIntervalMinutes);
            options.KeepAliveInterval = TimeSpan.FromSeconds(signalRConfig.KeepAliveIntervalSeconds);
            options.HandshakeTimeout = TimeSpan.FromSeconds(signalRConfig.HandshakeTimeoutSeconds);
            options.MaximumReceiveMessageSize = signalRConfig.MaximumReceiveMessageSizeBytes;
            options.EnableDetailedErrors = applicationBuilder.Environment.IsDevelopment();
        });

        return services;
    }
}

internal static class ApiControllerRegistration
{
    internal static IServiceCollection AddApiControllers(this IServiceCollection services)
    {
        services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider<IdentityUser>>();
        services.AddControllers(options =>
        {
            var policy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes("Identity.Application", JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();
            options.Filters.Add(new AuthorizeFilter(policy));
            options.Filters.Add<GlobalExceptionFilter>();
        })
        .AddJsonOptions(AutomatonJsonOptions.Configure);
        services.AddTransient<UserNameEnricher>();
        services.AddHttpContextAccessor();
        return services;
    }
}

internal static class ApiLoggingRegistration
{
    internal static IServiceCollection ConfigureApiLogging(this IServiceCollection services, WebApplicationBuilder applicationBuilder)
    {
        applicationBuilder.Host.UseSerilog((context, services, config) =>
        {
            var logConfig = ApiLoggerConfiguration.Create(config, services);
            ApiLoggerSinks.Configure(
                logConfig,
                applicationBuilder.Environment.IsDevelopment(),
                context.Configuration["Logging:File:Path"] ?? "/app/logs",
                context.Configuration.GetValue<int?>("Logging:File:RetainedFileCountLimit") ?? 14);
        });
        return services;
    }
}

internal static class ApiLoggerConfiguration
{
    internal static LoggerConfiguration Create(LoggerConfiguration configuration, IServiceProvider services)
    {
        return configuration
            .Destructure.UsingAttributes()
            .Destructure.JsonNetTypes()
            .Enrich.With<EventTypeEnricher>()
            .Enrich.With(services.GetService<UserNameEnricher>())
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning);
    }
}

internal static class ApiLoggerSinks
{
    internal static void Configure(LoggerConfiguration logConfig, bool isDevelopment, string logDirectory, int retainedFileCountLimit)
    {
        if (isDevelopment)
        {
            logConfig.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Debug);
        }
        else
        {
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
    }
}

internal static class ApiHangfireRegistration
{
    internal static IServiceCollection AddApiHangfire(this IServiceCollection services, WebApplicationBuilder applicationBuilder, ConfigurationService configurationService)
    {
        var configurationManager = applicationBuilder.Configuration;
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
        return services;
    }
}

internal static class ApiAppCatalogRegistration
{
    internal static IServiceCollection AddApiAppCatalogServices(this IServiceCollection services)
    {
        services.AddScoped<CustomStepsService>();
        services.AddScoped<CustomStepRunnerExecutionService>();
        services.AddScoped<AppForkingService>();
        services.AddScoped<AppMutationService>();
        services.AddScoped<AppMutationAuditService>();
        services.AddScoped<AppDeletionService>();
        services.AddScoped<AppMetadataService>();
        services.AddScoped<AppQueryService>();
        services.AddScoped<AppPublicationService>();
        services.AddScoped<AppsService>();
        services.AddScoped<AppImageManagementService>();
        services.AddScoped<AppRuntimeArchiveUploadService>();
        services.AddScoped<IAppDeploymentTargetResolver, AppDeploymentTargetResolver>();
        services.AddScoped<AppFileRunnerResolver>();
        services.AddScoped<AppFileUploadService>();
        services.AddScoped<AppFileDownloadService>();
        services.AddScoped<AppDeploymentTargetStateService>();
        services.AddScoped<AppDeploymentTargetManagementService>();
        services.AddScoped<AppDeploymentTargetRuntimeCleanupService>();
        services.AddScoped<Automaton.Api.Controllers.AppDeploymentTargetExceptionFilter>();
        services.AddScoped<IAppRunnerAuthorizationPolicy, AppRunnerAuthorizationPolicy>();
        services.AddScoped<AppRunnerAssignmentPolicy>();
        services.AddScoped<AdminAppsService>();
        services.AddScoped<PublicAppsService>();
        services.AddScoped<PublicMarketplaceCreatorAppsQuery>();
        services.AddScoped<PublicMarketplaceCatalogQuery>();
        services.AddScoped<WebAppStarService>();
        services.AddScoped<AppStepsService>();
        services.AddScoped<AppsStepExecutionService>();
        services.AddSingleton<TunnelRequestRegistry>();
        return services;
    }
}

internal static class ApiAppExecutionRegistration
{
    internal static IServiceCollection AddApiAppExecutionServices(this IServiceCollection services, WebApplicationBuilder applicationBuilder)
    {
        services.AddOptions<DeploymentPackageStorageOptions>()
            .BindConfiguration(DeploymentPackageStorageOptions.SectionName)
            .Validate(options => options.MaximumStoredBytes > 0, "Deployment package storage byte limit must be positive.")
            .Validate(options => options.MaximumStoredPackages > 0, "Deployment package file-count limit must be positive.")
            .ValidateOnStart();
        services.Configure<RuntimeTunnelConfig>(applicationBuilder.Configuration.GetSection("RuntimeTunnel"));
        services.AddScoped<RuntimeTunnelRequestSender>();
        services.AddScoped<RuntimeTunnelService>();
        services.AddSingleton<FileUploadRegistry>();
        services.AddSingleton<FileDownloadRegistry>();
        services.AddScoped<RuntimeFileProxyService>();
        services.AddScoped<ApplicationDeploymentService>();
        services.AddScoped<DeploymentPackageFinalizationService>();
        services.AddScoped<DeploymentPackageRunnerService>();
        services.AddScoped<DeploymentPackageStore>();
        services.AddScoped<HostedRunnerDeploymentCommandPublisher>();
        services.AddScoped<HostedRunnerDeploymentCommandService>();
        services.AddScoped<IHostedRunnerDeploymentCommandDispatcher>(provider =>
            provider.GetRequiredService<HostedRunnerDeploymentCommandService>());
        services.AddScoped<HostedRunnerDeploymentResultService>();
        services.AddScoped<HostContainerExecutionOperationService>();
        services.AddScoped<HostContainerRelayService>();
        services.AddScoped<HostContainerRelayStateResolver>();
        services.AddScoped<ConnectedFlowArtifactService>();
        services.AddScoped<DeploymentOperationService>();
        services.AddHostedService<DeploymentOperationDispatcher>();
        services.AddScoped<AppActionExecutionService>();
        services.AddScoped<AppActionExecutionDispatcher>();
        services.AddScoped<AppActionRunnerGateway>();
        services.AddScoped<ActionScheduleService>();
        services.AddScoped<IActionScheduleTargetResolver, ActionScheduleTargetResolver>();
        services.AddScoped<ITrustedSystemActionScheduleDispatchValidator, TrustedSystemActionScheduleDispatchValidator>();
        services.AddSingleton<ITrustedSystemActionManifestSource, TrustedSystemActionManifestSource>();
        services.AddScoped<ScheduledActionExecutionHandler>();
        services.AddHostedService<AppModuleExecutionReconciliationService>();
        services.AddHostedService<HostedRunnerRestartReconciliationService>();
        services.AddHostedService<HostedRunnerLifecycleReconciliationService>();
        services.AddHostedService<ManagedRuntimeRouteReconciliationService>();
        services.AddScoped<IAppDeployCapabilityService, AppDeployCapabilityService>();
        services.AddScoped(typeof(UserImpersonationServiceFactory<>));
        services.AddScoped<FlowsService>();
        return services;
    }
}

internal static class ApiRunnerRegistration
{
    internal static IServiceCollection AddApiRunnerServices(this IServiceCollection services, WebApplicationBuilder applicationBuilder)
    {
        services.AddScoped<RunnerService>();
        services.AddScoped<RunnerStorageTelemetryService>();
        services.AddScoped<RunnerStorageTelemetrySubmissionService>();
        services.Configure<StorageTelemetryOptions>(applicationBuilder.Configuration.GetSection("StorageTelemetry"));
        services.AddScoped<RunnerEnrollmentService>();
        services.AddScoped<RunnerEnrollmentAdministrationService>();
        services.AddScoped<HostedRunnerEnrollmentProvisioningService>();
        services.AddScoped<HostedRunnerEnrollmentExchangeService>();
        services.AddScoped<InfrastructureRunnerEnrollmentExchangeService>();
        services.AddScoped<SelfHostedRunnerInstallationService>();
        services.AddScoped<SelfHostedRunnerEnrollmentExchangeService>();
        services.AddScoped<InfrastructureRunnerRegistrationService>();
        services.AddScoped<InfrastructureRunnerRabbitMqConfigurationService>();
        services.AddScoped<HostedRunnerBrokerConfigurationService>();
        services.AddScoped<HostedRunnerBrokerProvisioner>();
        services.AddScoped<InfrastructureRunnerHostApprovalService>();
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
        services.AddScoped<ExternalMcpToolExecutionService>();
        services.AddScoped<AppMediaStorageService>();
        services.AddScoped<PublishedAppStorageService>();
        services.AddScoped<AppArchiveService>();
        services.AddScoped<AppVersionService>();
        services.AddSingleton<PrivateAppLaunchService>();
        services.AddSingleton<RuntimeCookieService>();
        services.AddScoped<BackendRouteConfigService>();
        services.AddScoped<ManagedRunnerBootstrapService>();
        services.AddScoped<ManagedRunnerAppAssignmentService>();
        services.AddScoped<HostedRunnerAppCapacityService>();
        return services;
    }
}

internal static class ApiEmailRegistration
{
    internal static IServiceCollection AddApiEmailService(this IServiceCollection services, WebApplicationBuilder applicationBuilder, ConfigurationService configurationService)
    {
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
        return services;
    }
}

internal static class ApiAccountRegistration
{
    internal static IServiceCollection AddApiAccountServices(this IServiceCollection services)
    {
        services.AddScoped<InstallService>();
        services.AddScoped<SettingsService>();
        services.AddTransient<UsersService>();
        services.AddTransient<UserHardDeleteService>();
        services.AddScoped<ConfirmedEmailGuardService>();
        services.AddScoped<AuthTokenService>();
        services.AddTransient<RoleManagerService>();
        services.AddTransient<ScheduleService>();
        return services;
    }
}

internal static class ApiOperationsRegistration
{
    internal static IServiceCollection AddApiOperationsServices(this IServiceCollection services)
    {
        services.AddScoped<ApiKeyService>();
        services.AddScoped<AiApiKeyService>();
        services.AddScoped<AdminAiProviderCreationService>();
        services.AddScoped<AiProviderMetadataService>();
        services.AddScoped<AiConversationProviderResolver>();
        services.AddScoped<AiConversationHistoryPersistenceService>();
        services.AddScoped<IAiGuidanceRuntimeService, AiGuidanceRuntimeService>();
        services.AddScoped<IAutomatonGuidanceRuntimeProvider, AiGuidanceRuntimeService>();
        services.AddScoped<AdminAiGuidanceService>();
        services.AddScoped<AdminDocumentationService>();
        services.AddScoped<InfrastructureRunnerRemoteCommandExecutor>();
        services.AddScoped<IInfrastructureRunnerCommandExecutor>(sp => sp.GetRequiredService<InfrastructureRunnerRemoteCommandExecutor>());
        services.AddScoped<HostedRunnerHostPlacementService>();
        services.AddScoped<HostedRunnerProvisioningPolicyService>();
        services.AddScoped<HostedRunnerProvisioningPreparationService>();
        services.AddScoped<HostedRunnerStorageLocalityResolver>();
        services.AddScoped<HostedRunnerProvisioningService>();
        services.AddScoped<RunnerAuditService>();
        services.AddScoped<RunnerRegistrationService>();
        services.AddScoped<RunnerDeletionService>();
        services.AddScoped<RunnerDeletionPolicy>();
        services.AddScoped<RunnerConnectionSessionService>();
        services.AddScoped<RunnerQueryService>();
        services.AddScoped<HostedRunnerRestartCompletionService>();
        services.AddScoped<RunnerFlowExecutionService>();
        services.AddScoped<RunnerInstanceMigrationService>();
        services.AddScoped<RunnerConnectionLifecycleService>();
        services.AddScoped<HostedRunnerPlanCapabilityValidator>();
        services.AddScoped<AdminSubscriptionPlanOperationsService>();
        services.AddScoped<IHostedRunnerSpecificationResolver, HostedRunnerSpecificationResolver>();
        services.AddScoped<ISubscriptionResourcePolicyResolver, SubscriptionResourcePolicyResolver>();
        services.AddScoped<IAccountStorageProjectAllocationService, AccountStorageProjectAllocationService>();
        services.AddScoped<HostedRunnerManagementService>();
        services.AddScoped<InfrastructureWorkflowExecutionService>();
        services.AddScoped<LocalRunnerHostBootstrapService>();
        return services;
    }
}

internal static class ApiAiPromptRegistration
{
    internal static IServiceCollection AddApiAiPromptServices(this IServiceCollection services)
    {
        // System Prompt Builders for AI Cha
        services.AddScoped<Automaton.Mcp.SystemPrompts.IStepSystemPromptBuilder, Automaton.Mcp.SystemPrompts.StepSystemPromptBuilder>();
        services.AddScoped<Automaton.Mcp.SystemPrompts.IAppVerificationPromptStrategy, Automaton.Mcp.SystemPrompts.ContinuousVerificationPromptStrategy>();
        services.AddScoped<Automaton.Mcp.SystemPrompts.IAppVerificationPromptStrategy, Automaton.Mcp.SystemPrompts.BalancedVerificationPromptStrategy>();
        services.AddScoped<Automaton.Mcp.SystemPrompts.IAppVerificationPromptStrategy, Automaton.Mcp.SystemPrompts.FinalGateVerificationPromptStrategy>();
        services.AddScoped<Automaton.Mcp.SystemPrompts.IAppExecutionPolicyPromptStrategy, Automaton.Mcp.SystemPrompts.DefaultExecutionPolicyPromptStrategy>();
        services.AddScoped<Automaton.Mcp.SystemPrompts.IAppExecutionPolicyPromptStrategy, Automaton.Mcp.SystemPrompts.LeanToolsExecutionPolicyPromptStrategy>();
        services.AddSingleton<IAutomatonGuidanceProvider, EmbeddedAutomatonGuidanceProvider>();
        services.AddSingleton<Automaton.Mcp.SystemPrompts.IAppPromptContextSelector, Automaton.Mcp.SystemPrompts.KeywordAppPromptContextSelector>();
        services.AddScoped<Automaton.Mcp.SystemPrompts.IAppSystemPromptBuilder, Automaton.Mcp.SystemPrompts.AppSystemPromptBuilder>();
        return services;
    }
}

internal static class ApiMcpToolRegistration
{
    internal static IServiceCollection AddApiMcpToolServices(this IServiceCollection services)
    {
        // MCP Tool Provider for step AI conversations
        services.AddScoped<StepMcpToolProvider>();
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
        return services;
    }
}

internal static class ApiAiClientRegistration
{
    internal static IServiceCollection AddApiAiClientServices(this IServiceCollection services, WebApplicationBuilder applicationBuilder)
    {
        // AI providers + MCP clien
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
        services.AddScoped<StripeWebhookService>();
        services.AddScoped<StripeService>();
        return services;
    }
}

internal static class ApiProjectRegistration
{
    internal static IServiceCollection AddApiProjectServices(this IServiceCollection services)
    {
        // Project/Organization services
        services.AddScoped<ProjectService>();
        services.AddScoped<ProjectMemberService>();
        services.AddScoped<ProjectResourceService>();
        services.AddScoped<ProjectResourceAuthorizationService>();
        services.AddScoped<ProjectCredentialResourceService>();
        services.AddScoped<InvitationTokenService>();
        services.AddScoped<ResourceAuthorizationService>();
        services.AddScoped<SensitiveVariableService>();
        services.AddScoped<UserSettingsService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<OrganizationAiPolicySettingsService>();
        services.AddScoped<OrganizationInvitationNotificationService>();

        // Project authorization handlers
        services.AddScoped<IAuthorizationHandler, ProjectMemberHandler>();
        services.AddScoped<IAuthorizationHandler, ProjectAdminHandler>();
        services.AddScoped<IAuthorizationHandler, ProjectResourceManagerHandler>();
        services.AddScoped<IAuthorizationHandler, ProjectAppExecutorHandler>();
        return services;
    }
}

internal static class ApiFoundationRegistration
{
    internal static IServiceCollection AddApiFoundationServices(this IServiceCollection services, ConfigurationService configurationService)
    {
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
        return services;
    }
}

internal static class ApiCorsRegistration
{
    internal static IServiceCollection AddApiCors(this IServiceCollection services, string[]? corsOrigins)
    {
        if (corsOrigins is { Length: > 0 })
        {
            services.AddCors(options => options.AddPolicy("CorsPolicy", builder =>
            {
                builder
                    .WithOrigins(corsOrigins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            }));
        }

        return services;
    }
}
