using Automaton.Runner.Console.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Runner.Console.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.CommandLine;
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
        description: "Enable verbose output",
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
                CreateCleanupCommand()
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
            await runnerService.StartServiceModeAsync(
                host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
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

    private static IConfiguration GetConfiguration(string[] args)
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
    }

    internal static IHostBuilder CreateHostBuilder(string[] args)
        => RunnerHostBuilder.Create(args);

    private static void PressAnyKeyToExit()
    {
        Write("Press any key to exit");
        ReadKey();
    }
}
