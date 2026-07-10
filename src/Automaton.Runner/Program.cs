using Automaton.Runner.Console.Services;
using Automaton.Core.Enums;
using Automaton.Core.Interfaces;
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
                CreateExecuteAppFlowCommand()
            };

            rootCommand.AddGlobalOption(verboseOption);

            // Default action when no command is specified - start interactive runner
            rootCommand.SetHandler(async (bool verbose) =>
            {
                using var host = CreateHostBuilder(Array.Empty<string>()).Build();
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
            if (verbose)
            {
                PythonEnvironmentDiagnostics.DetectAndPrintIfVerbose(true);
            }
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

        var inputsJsonOption = new Option<string>(
            name: "--inputs-json",
            description: "JSON object of string inputs")
        {
            IsRequired = false
        };

        var command = new Command("execute-app-flow", "Execute a deployed app flow from local runner workspace")
        {
            appIdOption,
            flowIdentifierOption,
            inputsJsonOption
        };

        command.SetHandler(async (Guid appId, string flowIdentifier, string? inputsJson, bool verbose) =>
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

                var result = await appFlowExecutionService.ExecuteAsync(appId, flowIdentifier, inputs);
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
        }, appIdOption, flowIdentifierOption, inputsJsonOption, verboseOption);

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

    private static IHostBuilder CreateHostBuilder(string[] args) =>
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
                services.AddSingleton<RunnerBackendEnvironmentService>();
                services.AddSingleton<RunnerDeploymentService>();
                services.AddSingleton<RunnerBackendRegistry>();
                services.AddSingleton<RunnerBackendProcessManager>();
                services.AddSingleton<IAppBackendProcessManager>(sp => sp.GetRequiredService<RunnerBackendProcessManager>());
                services.AddHostedService(sp => sp.GetRequiredService<RunnerBackendProcessManager>());
                services.AddSingleton<RunnerRuntimeGatewayService>();
                services.AddHostedService(sp => sp.GetRequiredService<RunnerRuntimeGatewayService>());
                services.AddSingleton<HubService>();
                services.AddSingleton<FlowExecutionService>();
                services.AddSingleton<AppFlowExecutionService>();
                services.AddSingleton<RunnerHostService>();
                services.AddSingleton<RunnerWorkflowExecutionPersistenceService>();
                services.AddSingleton<IWorkflowExecutionPersistenceService, RunnerWorkflowExecutionPersistenceService>();

                // Register runner settings service for loading script engine configuration
                services.AddScoped<RunnerSettingsService>();

                // Register script engine settings reporter (replaces old manager)
                services.AddSingleton<ScriptEngineSettingsReporter>();

                // Register custom step handler as transient (uses scoped ScriptEngineHost)
                services.AddTransient<CustomStepExecuteService>();
                services.AddSingleton<TunnelForwarderService>();

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
                services.AddHttpClient<RunnerSettingsService>((sp, client) =>
                {
                    // Add authentication header if API key is available
                    var apiKeyService = sp.GetRequiredService<ApiKeyAuthenticationService>();
                    var apiKey = apiKeyService.GetValidApiKeyAsync().GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
                    }
                });

                // Add HTTP client for ScriptEngineSettingsReporter
                services.AddHttpClient<ScriptEngineSettingsReporter>((sp, client) =>
                {
                    var apiKeyService = sp.GetRequiredService<ApiKeyAuthenticationService>();
                    var apiKey = apiKeyService.GetValidApiKeyAsync().GetAwaiter().GetResult();
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
                    }
                });

                // Add default HTTP client
                services.AddHttpClient();

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
