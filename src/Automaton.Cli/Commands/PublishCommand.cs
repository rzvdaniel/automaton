using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton publish [--message "what changed"]
///
/// Creates a version snapshot of the app's current server-side files.
/// Useful to checkpoint before a new AI-assisted editing session.
/// </summary>
public static class PublishCommand
{
    public static Command Build(ConfigService config)
    {
        var messageOption = new Option<string?>(
            ["--message", "-m"],
            "One-sentence description of what changed");

        var yesOption = new Option<bool>("--yes", "Publish without prompting for confirmation");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("publish", "Save a version snapshot of the app's current files")
        {
            messageOption,
            yesOption,
            jsonOption
        };

        cmd.SetHandler(async (string? message, bool yes, bool asJson) =>
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.LoadProjectConfig(cwd);
                var (server, apiKey) = config.RequireCredentials(appConfig, cwd);

                if (!yes)
                {
                    Console.Write($"Publish the current server-side snapshot for '{appConfig.Name}'? [y/N]: ");
                    var confirmation = Console.ReadLine()?.Trim();
                    if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(confirmation, "yes", StringComparison.OrdinalIgnoreCase))
                    {
                        if (asJson)
                        {
                            CliJson.Write(new { success = false, cancelled = true, appId = appConfig.AppId });
                            return;
                        }

                        AnsiConsole.MarkupLine("[yellow]Publish cancelled.[/]");
                        return;
                    }
                }

                if (!asJson)
                {
                    AnsiConsole.MarkupLine($"[bold]Publishing snapshot[/] for [cyan]{appConfig.Name}[/]...");
                }

                using var client = new AutomatonApiClient(server, apiKey);
                var result = await client.PublishAsync(appConfig.AppId, message);

                var version = result.TryGetProperty("versionNumber", out var v) ? v.GetInt32().ToString() : "?";
                var summary = result.TryGetProperty("summary", out var s) ? s.GetString() : message;

                if (asJson)
                {
                    CliJson.Write(new { success = true, appId = appConfig.AppId, version, summary });
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✓[/] Version [bold]{version}[/] saved{(summary != null ? $": {summary}" : "")}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        }, messageOption, yesOption, jsonOption);

        return cmd;
    }
}
