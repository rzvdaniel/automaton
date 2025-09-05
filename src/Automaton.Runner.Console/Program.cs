using Automaton.Runner.Console.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Hosting;
using System.CommandLine;

namespace Automaton.Runner.Console;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Configure Serilog
        var configuration = GetConfiguration(args);
        
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        try
        {
            Log.Information("Starting Automaton Runner Console");

            var rootCommand = new RootCommand("Automaton Runner - Command Line Interface")
            {
                CreateRunCommand(),
                CreateAuthCommand(),
                CreateRegisterCommand(),
                CreateStatusCommand(),
                CreateCleanupCommand()
            };

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

    private static Command CreateRunCommand()
    {
        var nameOption = new Option<string>(
            name: "--name",
            description: "Runner name")
        {
            IsRequired = true
        };

        var serverOption = new Option<string>(
            name: "--server",
            description: "Server URL (e.g., https://localhost:7091)")
        {
            IsRequired = false
        };

        var command = new Command("run", "Start the runner and connect to the server")
        {
            nameOption,
            serverOption
        };

        command.SetHandler(async (string name, string server) =>
        {
            // Pass the server URL via command line arguments so it can be picked up by configuration
            var args = new List<string> { "--name", name };
            if (!string.IsNullOrEmpty(server))
            {
                args.AddRange(new[] { "--AppConfig:BaseUrl", server });
            }
            
            using var host = CreateHostBuilder(args.ToArray()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.StartAsync(name, server);
        }, nameOption, serverOption);

        return command;
    }

    private static Command CreateAuthCommand()
    {
        var apiKeyOption = new Option<string>(
            name: "--api-key",
            description: "API key for authentication (e.g., ak_xxxxxxxxxxxx)")
        {
            IsRequired = true
        };

        var serverOption = new Option<string>(
            name: "--server",
            description: "Server URL (e.g., https://localhost:7091)")
        {
            IsRequired = false
        };

        var command = new Command("auth", "Authenticate using API key")
        {
            apiKeyOption,
            serverOption
        };

        command.SetHandler(async (string apiKey, string server) =>
        {
            var args = Array.Empty<string>();
            if (!string.IsNullOrEmpty(server))
            {
                args = new[] { "--AppConfig:BaseUrl", server };
            }
            
            using var host = CreateHostBuilder(args).Build();
            using var scope = host.Services.CreateScope();
            var apiKeyAuthService = scope.ServiceProvider.GetRequiredService<ApiKeyAuthenticationService>();
            await apiKeyAuthService.SetApiKeyAsync(apiKey, server);
        }, apiKeyOption, serverOption);

        return command;
    }

    private static Command CreateRegisterCommand()
    {
        var nameOption = new Option<string>(
            name: "--name",
            description: "Runner name")
        {
            IsRequired = true
        };

        var serverOption = new Option<string>(
            name: "--server",
            description: "Server URL (e.g., https://localhost:7091)")
        {
            IsRequired = false
        };

        var command = new Command("register", "Register this runner with the server")
        {
            nameOption,
            serverOption
        };

        command.SetHandler(async (string name, string server) =>
        {
            var args = new List<string> { "--name", name };
            if (!string.IsNullOrEmpty(server))
            {
                args.AddRange(new[] { "--AppConfig:BaseUrl", server });
            }
            
            using var host = CreateHostBuilder(args.ToArray()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.RegisterAsync(name, server);
        }, nameOption, serverOption);

        return command;
    }

    private static Command CreateStatusCommand()
    {
        var command = new Command("status", "Show runner status and configuration");

        command.SetHandler(async () =>
        {
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var configService = scope.ServiceProvider.GetRequiredService<ConfigurationService>();
            var storageService = scope.ServiceProvider.GetRequiredService<FileStorageService>();
            var apiKeyAuthService = scope.ServiceProvider.GetRequiredService<ApiKeyAuthenticationService>();
            
            System.Console.WriteLine("=== Automaton Runner Status ===");
            System.Console.WriteLine($"Runner ID: {storageService.GetRunnerId() ?? "Not set"}");
            System.Console.WriteLine($"Runner Name: {storageService.GetRunnerName() ?? "Not set"}");
            System.Console.WriteLine($"Server URL: {configService.BaseUrl}");
            System.Console.WriteLine($"Registered: {storageService.IsRunnerRegistered()}");
            
            var isAuthenticated = await apiKeyAuthService.IsAuthenticatedAsync();
            var hasEnvKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AUTOMATON_API_KEY"));
            var hasStoredKey = !string.IsNullOrEmpty(await storageService.LoadApiKeyAsync());
            
            System.Console.WriteLine($"Authenticated: {isAuthenticated}");
            if (isAuthenticated)
            {
                System.Console.WriteLine($"Auth Source: {(hasEnvKey ? "Environment Variable" : hasStoredKey ? "Stored API Key" : "Unknown")}");
            }
            System.Console.WriteLine("================================");
        });

        return command;
    }

    private static Command CreateCleanupCommand()
    {
        var command = new Command("cleanup", "Cleanup resources used by the runner");

        command.SetHandler(async () =>
        {
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            await runnerService.CleanupAsync();
        });

        return command;
    }

    private static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                services.Configure<AppConfig>(context.Configuration.GetSection("AppConfig"));
                services.AddHttpClient();
                services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
                
                // Register services
                services.AddSingleton<ConfigurationService>();
                services.AddSingleton<FileStorageService>();
                services.AddScoped<ApiKeyAuthenticationService>();
                services.AddScoped<HubService>();
                services.AddScoped<FlowExecutionService>();
                services.AddScoped<RunnerHostService>();
            });

    private static IConfiguration GetConfiguration(string[] args)
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
    }
}