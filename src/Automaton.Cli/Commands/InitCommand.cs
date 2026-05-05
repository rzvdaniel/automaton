using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton init              — create a new app and write automaton.json
/// automaton init --app &lt;id&gt;  — link the current directory to an existing app
/// </summary>
public static class InitCommand
{
    public static Command Build(ConfigService config)
    {
        var appOption = new Option<string?>(
            ["--app", "-a"],
            "Link to an existing app ID instead of creating a new one");

        var nameOption = new Option<string?>(
            ["--name", "-n"],
            "App name (required when creating a new app)");

        var cmd = new Command("init", "Create a new Automaton app or link an existing one to this directory")
        {
            appOption,
            nameOption
        };

        cmd.SetHandler(async (string? appId, string? name) =>
        {
            try
            {
                var (server, apiKey) = config.RequireCredentials();
                var cwd = Directory.GetCurrentDirectory();

                using var client = new AutomatonApiClient(server, apiKey);

                if (!string.IsNullOrWhiteSpace(appId))
                {
                    // Link to existing app
                    AnsiConsole.MarkupLine($"[grey]Fetching app {appId}...[/]");
                    var app = await client.GetAppAsync(appId);
                    var appName = app.TryGetProperty("name", out var n) ? n.GetString()! : appId;

                    config.SaveProjectConfig(cwd, new AutomatonConfig { AppId = appId, Name = appName, Server = server });
                    AnsiConsole.MarkupLine($"[green]✓[/] Linked to app [bold]{appName}[/] ({appId})");
                    AnsiConsole.MarkupLine("[grey]Run 'automaton pull' to download the app's current files.[/]");
                    return;
                }

                // Create new app
                if (string.IsNullOrWhiteSpace(name))
                {
                    Console.Write("App name: ");
                    name = Console.ReadLine()?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        AnsiConsole.MarkupLine("[red]App name is required.[/]");
                        Environment.Exit(1);
                        return;
                    }
                }

                AnsiConsole.MarkupLine($"[grey]Creating app '{name}'...[/]");
                var (newAppId, newAppName) = await client.CreateAppAsync(name);

                config.SaveProjectConfig(cwd, new AutomatonConfig { AppId = newAppId, Name = newAppName, Server = server });

                AnsiConsole.MarkupLine($"[green]✓[/] Created app [bold]{newAppName}[/]");
                AnsiConsole.MarkupLine($"[grey]App ID:[/] {newAppId}");
                AnsiConsole.MarkupLine("[grey]automaton.json written.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        }, appOption, nameOption);

        return cmd;
    }
}
