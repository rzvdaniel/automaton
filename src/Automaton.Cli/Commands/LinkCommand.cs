using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class LinkCommand
{
    public static Command Build(ConfigService config)
    {
        var appIdArgument = new Argument<string>("app-id", "Existing Automaton app ID to link to this directory");

        var cmd = new Command("link", "Link this directory to an existing Automaton app")
        {
            appIdArgument
        };

        cmd.SetHandler(async (string appId) =>
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var (server, apiKey) = config.RequireCredentials(directory: cwd);

                using var client = new AutomatonApiClient(server, apiKey);

                AnsiConsole.MarkupLine($"[grey]Fetching app {appId}...[/]");
                var app = await client.GetAppAsync(appId);
                var appName = app.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString()! : appId;

                config.SaveProjectConfig(cwd, new AutomatonConfig { AppId = appId, Name = appName, Server = server });
                AnsiConsole.MarkupLine($"[green]✓[/] Linked to app [bold]{appName}[/] ({appId})");
                AnsiConsole.MarkupLine("[grey]Run 'automaton pull' to download the app's current files.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        }, appIdArgument);

        return cmd;
    }
}