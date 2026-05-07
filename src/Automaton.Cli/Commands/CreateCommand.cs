using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class CreateCommand
{
    public static Command Build(ConfigService config)
    {
        var nameOption = new Option<string?>(["--name", "-n"], "App name");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("create", "Create a new Automaton app and write automaton.json")
        {
            nameOption,
            jsonOption
        };

        cmd.SetHandler(async (string? name, bool asJson) =>
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var (server, apiKey) = config.RequireCredentials(directory: cwd);

                if (string.IsNullOrWhiteSpace(name))
                {
                    Console.Write("App name: ");
                    name = Console.ReadLine()?.Trim();
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException("App name is required.");
                }

                using var client = new AutomatonApiClient(server, apiKey);
                var (appId, appName) = await client.CreateAppAsync(name);

                config.SaveProjectConfig(cwd, new AutomatonConfig
                {
                    AppId = appId,
                    Name = appName,
                    Server = server
                });

                if (asJson)
                {
                    CliJson.Write(new { success = true, appId, name = appName, server, configPath = Path.Combine(cwd, "automaton.json") });
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✓[/] Created app [bold]{appName}[/]");
                AnsiConsole.MarkupLine($"[grey]App ID:[/] {appId}");
                AnsiConsole.MarkupLine("[grey]automaton.json written.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        }, nameOption, jsonOption);

        return cmd;
    }
}