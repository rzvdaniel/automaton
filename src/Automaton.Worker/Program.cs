using Automaton.Worker.Consumers;
using Automaton.Worker.Handlers;
using Automaton.Worker.Messaging;
using Automaton.Worker.Services;
using Automaton.Messaging.Abstractions;
using Automaton.Messaging.Events;
using Automaton.Identity;
using Automaton.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Serilog;
using Serilog.Events;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, config) =>
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
        path: Path.Combine(logDirectory, "worker-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: retainedFileCountLimit,
        restrictedToMinimumLevel: LogEventLevel.Information,
        shared: true,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}");
});

builder.Services.AddAutomatonInfrastructure(builder.Configuration);
builder.Services.AddAutomatonWorkerIdentity();
builder.Services.Configure<Automaton.Worker.Config.HostedRunnerOptions>(builder.Configuration.GetSection("HostedRunners"));
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
        .SetApplicationName("Automaton.Worker");
}
builder.Services.AddSingleton<IEventPublisher, CapEventPublisher>();
builder.Services.AddTransient<UserRegisteredCapSubscriber>();
builder.Services.AddTransient<IEventHandler<UserRegisteredEvent>, UserRegisteredEventHandler>();
builder.Services.AddTransient<ManagedRunnerProvisionRequestedCapSubscriber>();
builder.Services.AddTransient<IEventHandler<ManagedRunnerProvisionRequestedEvent>, ManagedRunnerProvisionRequestedEventHandler>();
builder.Services.AddTransient<IHostedRunnerRemoteCommandExecutor, SshHostedRunnerRemoteCommandExecutor>();
builder.Services.AddTransient<IHostedRunnerRemoteCommandExecutor, LocalDockerHostedRunnerRemoteCommandExecutor>();
builder.Services.AddTransient<IHostedRunnerRemoteCommandExecutorResolver, HostedRunnerRemoteCommandExecutorResolver>();
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

    options.UseMySql(connectionString);
    options.UseRabbitMQ(rabbitMq =>
    {
        rabbitMq.HostName = rabbitMqHost;
        rabbitMq.UserName = rabbitMqUsername;
        rabbitMq.Password = rabbitMqPassword;
        rabbitMq.VirtualHost = builder.Configuration["RabbitMq:VirtualHost"] ?? "/";
    });
});

var host = builder.Build();
host.Run();
