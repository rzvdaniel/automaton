using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Identity;
using Automaton.Infrastructure;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Worker.Consumers;
using Automaton.Worker.Handlers;
using Automaton.Worker.Messaging;
using Microsoft.AspNetCore.DataProtection;
using Serilog;
using Serilog.Events;

namespace Automaton.Worker.Services;

internal static class WorkerHostApplication
{
    public static async Task RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        ConfigureLogging(builder);
        ConfigureServices(builder);
        ConfigureMessaging(builder);
        await builder.Build().RunAsync();
    }

    private static void ConfigureLogging(HostApplicationBuilder builder)
    {
        builder.Services.AddSerilog((_, config) =>
        {
            var logDirectory = builder.Configuration["Logging:File:Path"] ?? "Logs/worker";
            var retainedFileCountLimit = builder.Configuration.GetValue<int?>("Logging:File:RetainedFileCountLimit") ?? 14;

            var logConfig = config
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.DataProtection", LogEventLevel.Warning)
                .MinimumLevel.Override("DotNetCore.CAP", LogEventLevel.Warning);

            if (builder.Environment.IsDevelopment())
                logConfig.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Debug);
            else
                logConfig.WriteTo.Console(
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    restrictedToMinimumLevel: LogEventLevel.Information);

            logConfig.WriteTo.File(
                path: Path.Combine(logDirectory, "worker-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: retainedFileCountLimit,
                restrictedToMinimumLevel: LogEventLevel.Information,
                shared: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}");
        });
    }

    private static void ConfigureServices(HostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddAutomatonInfrastructure(builder.Configuration);
        services.AddAutomatonWorkerIdentity();
        services.Configure<Automaton.Worker.Config.HostedRunnerOptions>(builder.Configuration.GetSection("HostedRunners"));
        services.Configure<StorageTelemetryOptions>(builder.Configuration.GetSection("StorageTelemetry"));
        ConfigureDataProtection(builder);
        services.AddSingleton<IEventPublisher, CapEventPublisher>();
        services.AddScoped<ITransactionalEventExecutor, CapTransactionalEventExecutor>();
        services.AddScoped<HostedRunnerRegistrationReconciler>();
        services.AddScoped<HostedRunnerStorageReconciliationService>();
        services.AddScoped<ActionScheduleProcessor>();
        services.AddScoped<WorkerScheduledActionDispatcher>();
        services.AddScoped<IWorkerScheduledActionHandler, ReconcileHostedRunnerStorageScheduledActionHandler>();
        services.AddScoped<IWorkerScheduledActionHandler, ReconcileRunnerHostStorageScheduledActionHandler>();
        services.AddScoped<IWorkerScheduledActionHandler, EnforceHostedRunnerStorageScheduledActionHandler>();
        services.AddScoped<ITrustedSystemActionScheduleDispatchValidator, TrustedSystemActionScheduleDispatchValidator>();
        services.AddSingleton<ITrustedSystemActionManifestSource, TrustedSystemActionManifestSource>();
        services.AddTransient<UserRegisteredCapSubscriber>();
        services.AddTransient<IEventHandler<UserRegisteredEvent>, UserRegisteredEventHandler>();
        services.AddTransient<ManagedRunnerProvisionRequestedCapSubscriber>();
        services.AddTransient<IEventHandler<ManagedRunnerProvisionRequestedEvent>, ManagedRunnerProvisionRequestedEventHandler>();
        services.AddHostedService<CliAuthorizationCleanupService>();
        services.AddHostedService<HostedRunnerRegistrationReconciliationService>();
        services.AddHostedService<ActionSchedulePollingService>();
        services.AddHostedService<BuiltInStorageScheduleSeeder>();
    }

    private static void ConfigureDataProtection(HostApplicationBuilder builder)
    {
        var keysPath = builder.Configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(keysPath)) return;

        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetApplicationName("Automaton.Worker");
    }

    private static void ConfigureMessaging(HostApplicationBuilder builder)
    {
        builder.Services.AddCap(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString("MySqlConnection")
                ?? throw new InvalidOperationException("ConnectionStrings:MySqlConnection is required.");
            var rabbitMqHost = builder.Configuration["RabbitMq:Host"]
                ?? throw new InvalidOperationException("RabbitMq:Host is required.");
            var rabbitMqUsername = builder.Configuration["RabbitMq:Username"]
                ?? throw new InvalidOperationException("RabbitMq:Username is required.");
            var rabbitMqPassword = builder.Configuration["RabbitMq:Password"]
                ?? throw new InvalidOperationException("RabbitMq:Password is required.");

            CapRegistration.ConfigureStorage(options, connectionString);
            options.UseRabbitMQ(rabbitMq => ConfigureRabbitMq(builder, rabbitMq, rabbitMqHost, rabbitMqUsername, rabbitMqPassword));
        });
    }

    private static void ConfigureRabbitMq(
        HostApplicationBuilder builder,
        DotNetCore.CAP.RabbitMQOptions rabbitMq,
        string host,
        string username,
        string password)
    {
        rabbitMq.HostName = host;
        rabbitMq.Port = int.TryParse(builder.Configuration["RabbitMq:Port"], out var port) ? port : 5672;
        rabbitMq.UserName = username;
        rabbitMq.Password = password;
        rabbitMq.VirtualHost = builder.Configuration["RabbitMq:VirtualHost"] ?? "/";
    }
}
