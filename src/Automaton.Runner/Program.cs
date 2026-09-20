using Automaton.Runner.Console.Services;
using Automaton.Core.Enums;
using Automaton.Core.Interfaces;
using Automaton.Core.Models;
using Automaton.Runner.Console.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.CommandLine;
using Automaton.Core.Scripting;
using Automaton.Core.Services;
using Microsoft.Extensions.Http;
using static System.Console;

namespace Automaton.Runner.Console;

public static class VerboseOptions
{
    public static bool Verbose { get; set; } = false;
}

public class Program
{
    private static readonly System.Text.Json.JsonSerializerOptions jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    private static readonly Option<bool> verboseOption = new Option<bool>(
        aliases: new[] { "--verbose", "-v" },
        description: "Enable verbose output (prints Python environment details)",
        getDefaultValue: () => false
    );

    public static async Task<int> Main(string[] args)
    {
        var configuration = GetConfiguration(args);

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        try
        {
            Log.Information("Starting Automaton Runner Console");

            var rootCommand = new RootCommand("Automaton Runner - Command Line Interface")
            {
                CreateAuthCommand(),
                CreateRegisterCommand(),
                CreateStatusCommand(),
                CreateServerCommand(),
                CreateServiceCommand(),
                CreateCleanupCommand(),
                CreateInfrastructureCommandProbe(),
                CreateExecuteAppFlowCommand()
            };

            rootCommand.AddGlobalOption(verboseOption);

            // Default action when no command is specified - start interactive runner
            rootCommand.SetHandler(async (bool verbose) =>
            {
                using var host = CreateHostBuilder(Array.Empty<string>()).Build();
                await host.StartAsync();
                using var scope = host.Services.CreateScope();
                var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
                await runnerService.StartInteractiveAsync();
#if DEBUG
                PressAnyKeyToExit();
#endif
            }, verboseOption);

            // All subcommands will inherit the verbose option from the root
            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static Command CreateInfrastructureCommandProbe()
    {
        var command = new Command("infrastructure-command-probe", "Consume one local infrastructure command without acknowledging it. Development only.");
        command.SetHandler(async (System.CommandLine.Invocation.InvocationContext context) =>
        {
            if (!string.Equals(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), "Development", StringComparison.Ordinal))
                throw new InvalidOperationException("The infrastructure command probe requires DOTNET_ENVIRONMENT=Development.");
            var identity = await InfrastructureCommandIdentity.LoadAsync(new FileStorageService());
            var workspace = new RunnerWorkspaceService();
            var coordinator = new AppActionExecutionCoordinator();
            var configuration = GetConfiguration(Array.Empty<string>());
            var api = new InfrastructureCommandApiClient(configuration, new FileStorageService());
            var executor = new TrustedInfrastructureActionExecutor(
                new AppActionDeploymentService(workspace, coordinator), new AppActionExecutionService(workspace, coordinator), api);
            var consumer = new InfrastructureCommandConsumer(identity, executor, api);
            var result = await consumer.ConsumeOneAsync(configuration, context.GetCancellationToken());
            WriteLine(System.Text.Json.JsonSerializer.Serialize(new { result.Execution, result.Result }, jsonSerializerOptions));
        });
        return command;
    }

    private static Command CreateAuthCommand()
    {
        var apiKeyOption = new Option<string>(
            name: "--api-key",
            description: "API key for authentication (optional - will prompt if not provided)")
        {
            IsRequired = false
        };

        var serverOption = new Option<string>(
            name: "--server",
            description: "Server URL (optional - will prompt if not provided)")
        {
            IsRequired = false
        };

        var command = new Command("auth", "Authenticate using API key (interactive if no options provided)")
        {
            apiKeyOption,
            serverOption
        };

        command.SetHandler(async (string? apiKey, string? server, bool verbose) =>
        {
            VerboseOptions.Verbose = verbose;
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.AuthenticateInteractiveAsync(apiKey, server);
#if DEBUG
            PressAnyKeyToExit();
#endif
        }, apiKeyOption, serverOption, verboseOption);

        return command;
    }

    private static Command CreateRegisterCommand()
    {
        var nameOption = new Option<string>(
            name: "--name",
            description: "Runner name (optional if already configured)")
        {
            IsRequired = false
        };

        var forceOption = new Option<bool>(
            name: "--force",
            description: "Force re-registration even if already registered")
        {
            IsRequired = false
        };

        var command = new Command("register", "Register runner with server (requires prior authentication)")
        {
            nameOption,
            forceOption
        };

        command.SetHandler(async (string? name, bool force, bool verbose) =>
        {
            VerboseOptions.Verbose = verbose;
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.RegisterInteractiveAsync(name, force);
#if DEBUG
            PressAnyKeyToExit();
#endif
        }, nameOption, forceOption, verboseOption);

        return command;
    }

    private static Command CreateStatusCommand()
    {
        var command = new Command("status", "Show runner configuration and status");

        command.SetHandler(async (bool verbose) =>
        {
            VerboseOptions.Verbose = verbose;
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.ShowStatusAsync();
#if DEBUG
            PressAnyKeyToExit();
#endif
        }, verboseOption);

        return command;
    }

    private static Command CreateServerCommand()
    {
        var urlOption = new Option<string>(
            name: "--url",
            description: "Server URL to configure")
        {
            IsRequired = false
        };

        var command = new Command("server", "Configure server URL")
        {
            urlOption
        };

        command.SetHandler(async (string? url, bool verbose) =>
        {
            VerboseOptions.Verbose = verbose;
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.ConfigureServerAsync(url);
#if DEBUG
            PressAnyKeyToExit();
#endif
        }, urlOption, verboseOption);

        return command;
    }

    private static Command CreateServiceCommand()
    {
        var command = new Command("service", "Run in non-interactive service mode (for Windows services, systemd, etc.)");

        command.SetHandler(async (bool verbose) =>
        {
            VerboseOptions.Verbose = verbose;
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            await host.StartAsync();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.StartServiceModeAsync();
        }, verboseOption);

        return command;
    }

    private static Command CreateCleanupCommand()
    {
        var command = new Command("cleanup", "Clean up stored API key and runner configuration");

        command.SetHandler(async (bool verbose) =>
        {
            VerboseOptions.Verbose = verbose;
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.CleanupAsync();
#if DEBUG
            PressAnyKeyToExit();
#endif
        }, verboseOption);

        return command;
    }

    private static Command CreateExecuteAppFlowCommand()
    {
        var appIdOption = new Option<Guid>(
            name: "--app-id",
            description: "App ID to execute flow for")
        {
            IsRequired = true
        };

        var flowIdentifierOption = new Option<string>(
            name: "--flow-identifier",
            description: "Flow ID or flow name")
        {
            IsRequired = true
        };

        var deploymentTargetIdOption = new Option<Guid>(
            name: "--deployment-target-id",
            description: "Deployment target ID to execute flow for")
        {
            IsRequired = true
        };

        var inputsJsonOption = new Option<string>(
            name: "--inputs-json",
            description: "JSON object of string inputs")
        {
            IsRequired = false
        };

        var command = new Command("execute-app-flow", "Execute a deployed app flow from local runner workspace")
        {
            appIdOption,
            deploymentTargetIdOption,
            flowIdentifierOption,
            inputsJsonOption
        };

        command.SetHandler(async (
            Guid appId,
            Guid deploymentTargetId,
            string flowIdentifier,
            string? inputsJson,
            bool verbose) =>
        {
            VerboseOptions.Verbose = verbose;
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var appFlowExecutionService = scope.ServiceProvider.GetRequiredService<AppFlowExecutionService>();

            try
            {
                var inputs = string.IsNullOrWhiteSpace(inputsJson)
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(inputsJson)
                        ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var result = await appFlowExecutionService.ExecuteAsync(
                    appId,
                    deploymentTargetId,
                    flowIdentifier,
                    inputs);
                WriteLine(System.Text.Json.JsonSerializer.Serialize(result, jsonSerializerOptions));
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                var error = new AppFlowExecutionResult
                {
                    Success = false,
                    Status = WorkflowStatus.Error.ToString(),
                    RunnerCount = 1,
                    ExecutedAt = DateTime.UtcNow,
                    Message = ex.Message
                };
                WriteLine(System.Text.Json.JsonSerializer.Serialize(error, jsonSerializerOptions));
                Environment.ExitCode = 1;
            }
        }, appIdOption, deploymentTargetIdOption, flowIdentifierOption, inputsJsonOption, verboseOption);

        return command;
    }

    private static IConfiguration GetConfiguration(string[] args)
    {
        // Allow self-signed certificates in development
        if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" ||
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Development")
        {
            System.Net.ServicePointManager.ServerCertificateValidationCallback =
                (sender, certificate, chain, sslPolicyErrors) => true;
        }

        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
    }

    internal static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.Configure<AppConfig>(context.Configuration.GetSection("AppConfig"));
                services.Configure<TunnelForwarderConfig>(context.Configuration.GetSection("TunnelForwarder"));

                // Register configuration services
                services.AddSingleton<ConfigurationService>();
                services.AddSingleton<FileStorageService>();
                services.AddSingleton<ApiKeyAuthenticationService>();
                services.AddSingleton<RunnerWorkspaceService>();
                services.AddSingleton<AppActionDeploymentService>();
                services.AddSingleton<AppActionExecutionService>();
                services.AddSingleton<AppActionExecutionCoordinator>();
                services.AddHostedService<AppActionArtifactCleanupService>();
                services.AddSingleton<RunnerDeploymentService>();
                services.AddSingleton<RunnerRuntimeRegistry>();
                services.AddSingleton<IApplicationContainerRuntime>(sp =>
                    RunnerApplicationContainerRuntimeSelection.UsesBroker(
                        Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_PURPOSE"),
                        Environment.GetEnvironmentVariable("AUTOMATON_RUNNER_APPLICATION_PROFILE"),
                        Environment.GetEnvironmentVariable("AUTOMATON_HOSTED_RUNNER_ID"))
                        ? new BrokeredApplicationContainerRuntime(
                            // Keep the Hub relay lazy: resolving it while the hosted
                            // runtime manager starts would cycle through RunnerDeploymentService.
                            (Func<HostContainerExecutionRequest, CancellationToken, Task<HostContainerExecutionResponse>>)((request, cancellationToken) =>
                                sp.GetRequiredService<HubService>().RequestHostContainerExecutionAsync(request, cancellationToken)),
                            (Func<Task<RunnerInstallationProfile?>>)(() =>
                                sp.GetRequiredService<FileStorageService>().LoadInstallationProfileAsync()))
                        : new DockerContainerRuntime());
                services.AddSingleton<IRunnerApplicationBuildExecutor, RunnerApplicationBuildExecutor>();
                services.AddSingleton<RunnerRuntimeContainerManager>();
                services.AddSingleton<IAppRuntimeProcessManager>(sp => sp.GetRequiredService<RunnerRuntimeContainerManager>());
                services.AddHostedService(sp => sp.GetRequiredService<RunnerRuntimeContainerManager>());
                services.AddSingleton<RunnerRuntimeProcessManager>();
                services.AddHostedService(sp => sp.GetRequiredService<RunnerRuntimeGatewayService>());
                services.AddSingleton<HubService>();
                services.AddHostedService<StorageTelemetryBackgroundService>();
                services.AddSingleton<FlowExecutionService>();
                services.AddSingleton<AppFlowExecutionService>();
                services.AddSingleton<RunnerHostService>();
                services.AddSingleton<RunnerWorkflowExecutionPersistenceService>();
                services.AddSingleton<IWorkflowExecutionPersistenceService, RunnerWorkflowExecutionPersistenceService>();

                // Register runner settings service for loading script engine configuration
                services.AddScoped<RunnerSettingsService>();

                // Register custom step handler as transient (uses scoped ScriptEngineHost)
                services.AddTransient<CustomStepExecuteService>();
                services.AddSingleton<InfrastructureCommandService>();
                services.AddSingleton<InfrastructureCommandConsumerStatus>();
                services.AddSingleton<HostContainerExecutionService>();
                services.AddSingleton<TunnelForwarderService>();
                services.AddHostedService<InfrastructureCommandHostedService>();

                // Configure HTTP client factory with SSL bypass for development
                var isDevelopment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Development" ||
                                   Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

                if (isDevelopment)
                {
                    // Configure all HTTP clients to bypass SSL validation in development
                    services.ConfigureAll<HttpClientFactoryOptions>(options =>
                    {
                        options.HttpMessageHandlerBuilderActions.Add(builder =>
                        {
                            builder.PrimaryHandler = new HttpClientHandler
                            {
                                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                            };
                        });
                    });
                }

                // Add HTTP client for RunnerHostService
                services.AddHttpClient<RunnerHostService>();

                // Add HTTP client for RunnerSettingsService
                services.AddHttpClient<RunnerSettingsService>();

                // Add HTTP client for ScriptEngineSettingsReporter
                services.AddHttpClient<ScriptEngineSettingsReporter>();

                // Add default HTTP client
                services.AddHttpClient();

                // The runtime gateway is a transparent proxy. It must not follow
                // application redirects on the runner's behalf.
                services.AddHttpClient<RunnerRuntimeGatewayService>()
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                    {
                        AllowAutoRedirect = false
                    });

                // Register MediatR - scan current assembly and Core assembly
                services.AddMediatR(cfg =>
                {
                    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
                    cfg.RegisterServicesFromAssembly(typeof(Core.Services.FlowExecuteService).Assembly);
                });

                // Register Automaton services with runner-specific script engine settings
                // Settings will be loaded at execution time to avoid deadlocks
                services.AddScriptingForRunner();
                services.AddAutomatonCore();
                services.AddAutomatonSteps();

                // Override FlowExecuteService with proper application name for console runner
                services.AddTransient<FlowExecuteService>(provider =>
                {
                    var flowConvertService = provider.GetRequiredService<FlowConvertService>();
                    var configService = provider.GetRequiredService<ConfigurationService>();
                    var notificationService = provider.GetService<IExecutionNotificationService>();
                    var persistenceService = provider.GetService<IWorkflowExecutionPersistenceService>();

                    return new FlowExecuteService(
                        flowConvertService,
                        configService.ApplicationName, // Use the proper application name from config
                        notificationService,
                        persistenceService);
                });
            });

    private static void PressAnyKeyToExit()
    {
        Write("Press any key to exit");
        ReadKey();
    }
}
