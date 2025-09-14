using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.CommandLine;
using Automaton.Core.Scripting;
using MediatR;

namespace Automaton.Runner.Console;

public class Program
{
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
                CreateCleanupCommand(),
                CreateServiceCommand()
            };

            // Default action when no command is specified - start interactive runner
            rootCommand.SetHandler(async () =>
            {
                using var host = CreateHostBuilder(Array.Empty<string>()).Build();
                using var scope = host.Services.CreateScope();
                var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
                await runnerService.StartInteractiveAsync();
            });

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

        command.SetHandler(async (string? apiKey, string? server) =>
        {
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.AuthenticateInteractiveAsync(apiKey, server);
        }, apiKeyOption, serverOption);

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

        command.SetHandler(async (string? name, bool force) =>
        {
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.RegisterInteractiveAsync(name, force);
        }, nameOption, forceOption);

        return command;
    }

    private static Command CreateStatusCommand()
    {
        var command = new Command("status", "Show runner configuration and status");

        command.SetHandler(async () =>
        {
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.ShowStatusAsync();
        });

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

        command.SetHandler(async (string? url) =>
        {
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.ConfigureServerAsync(url);
        }, urlOption);

        return command;
    }

    private static Command CreateCleanupCommand()
    {
        var command = new Command("cleanup", "Clean up stored API key and runner configuration");

        command.SetHandler(async () =>
        {
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.CleanupAsync();
        });

        return command;
    }

    private static Command CreateServiceCommand()
    {
        var serviceCommand = new Command("service", "Manage runner as a system service");
        
        serviceCommand.AddCommand(CreateInstallServiceCommand());
        serviceCommand.AddCommand(CreateUninstallServiceCommand());
        serviceCommand.AddCommand(CreateServiceStatusCommand());
        
        return serviceCommand;
    }

    private static Command CreateInstallServiceCommand()
    {
        var nameOption = new Option<string>(
            name: "--name",
            description: "Runner name for the service (optional)")
        {
            IsRequired = false
        };

        var noStartOption = new Option<bool>(
            name: "--no-start",
            description: "Install service but don't start it immediately")
        {
            IsRequired = false
        };

        var command = new Command("install", "Install runner as a system service")
        {
            nameOption,
            noStartOption
        };

        command.SetHandler(async (string? name, bool noStart) =>
        {
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var serviceManager = scope.ServiceProvider.GetRequiredService<ServiceManagementService>();
            await serviceManager.InstallServiceAsync(name, !noStart);
        }, nameOption, noStartOption);

        return command;
    }

    private static Command CreateUninstallServiceCommand()
    {
        var command = new Command("uninstall", "Uninstall the system service");

        command.SetHandler(async () =>
        {
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var serviceManager = scope.ServiceProvider.GetRequiredService<ServiceManagementService>();
            await serviceManager.UninstallServiceAsync();
        });

        return command;
    }

    private static Command CreateServiceStatusCommand()
    {
        var command = new Command("status", "Show system service status and management commands");

        command.SetHandler(async () =>
        {
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var serviceManager = scope.ServiceProvider.GetRequiredService<ServiceManagementService>();
            await serviceManager.ShowServiceStatusAsync();
        });

        return command;
    }

    private static IConfiguration GetConfiguration(string[] args)
    {
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
                // Register configuration services
                services.AddSingleton<ConfigurationService>();
                services.AddSingleton<FileStorageService>();
                services.AddSingleton<ApiKeyAuthenticationService>();
                services.AddSingleton<HubService>();
                services.AddSingleton<FlowExecutionService>();
                services.AddSingleton<RunnerHostService>();
                services.AddSingleton<ServiceManagementService>();

                // Add HTTP client
                services.AddHttpClient();

                // Register MediatR - scan current assembly and Core assembly
                services.AddMediatR(cfg => 
                {
                    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
                    cfg.RegisterServicesFromAssembly(typeof(Automaton.Core.Services.FlowExecuteService).Assembly);
                });

                // Register Automaton Core services
                services.AddAutomatonCore();
            });
}