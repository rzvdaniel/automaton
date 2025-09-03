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
                CreateLoginCommand(),
                CreateRegisterCommand(),
                CreateStatusCommand(),
                CreateClearCommand()
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

    private static Command CreateLoginCommand()
    {
        var serverOption = new Option<string>(
            name: "--server",
            description: "Server URL (e.g., https://localhost:7091)")
        {
            IsRequired = false
        };

        var command = new Command("login", "Login to the Automaton server")
        {
            serverOption
        };

        command.SetHandler(async (string server) =>
        {
            var args = Array.Empty<string>();
            if (!string.IsNullOrEmpty(server))
            {
                args = new[] { "--AppConfig:BaseUrl", server };
            }
            
            using var host = CreateHostBuilder(args).Build();
            using var scope = host.Services.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<ConsoleAuthenticationService>();
            await authService.LoginAsync(server);
        }, serverOption);

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

        command.SetHandler(() =>
        {
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            var configService = host.Services.GetRequiredService<ConfigurationService>();
            var storageService = host.Services.GetRequiredService<FileStorageService>();
            
            System.Console.WriteLine("Automaton Runner Status:");
            System.Console.WriteLine($"Runner ID: {storageService.GetRunnerId() ?? "Not set"}");
            System.Console.WriteLine($"Runner Name: {storageService.GetRunnerName() ?? "Not set"}");
            System.Console.WriteLine($"Server URL: {configService.BaseUrl}");
            System.Console.WriteLine($"Registered: {storageService.IsRunnerRegistered()}");
            System.Console.WriteLine($"Logged In: {storageService.IsLoggedIn()}");
        });

        return command;
    }

    private static Command CreateClearCommand()
    {
        var allOption = new Option<bool>(
            name: "--all",
            description: "Clear all stored data (credentials + runner registration)")
        {
            IsRequired = false
        };

        var command = new Command("clear", "Clear stored runner registration or all data")
        {
            allOption
        };

        command.SetHandler(async (bool clearAll) =>
        {
            using var host = CreateHostBuilder(Array.Empty<string>()).Build();
            using var scope = host.Services.CreateScope();
            var runnerService = scope.ServiceProvider.GetRequiredService<RunnerHostService>();
            
            if (clearAll)
            {
                System.Console.WriteLine("?? Clearing all stored data (credentials + runner registration)...");
                await runnerService.ClearAllDataAsync();
            }
            else
            {
                System.Console.WriteLine("?? Clearing runner registration...");
                await runnerService.ClearRunnerRegistrationAsync();
            }
        }, allOption);

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
                services.AddScoped<ConsoleAuthenticationService>();
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