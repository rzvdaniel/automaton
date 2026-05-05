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

        var cmd = new Command("publish", "Save a version snapshot of the app's current files")
        {
            messageOption
        };

        cmd.SetHandler(async (string? message) =>
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.LoadProjectConfig(cwd);
                var (server, apiKey) = config.RequireCredentials(appConfig);

                AnsiConsole.MarkupLine($"[bold]Publishing snapshot[/] for [cyan]{appConfig.Name}[/]...");

                using var client = new AutomatonApiClient(server, apiKey);
                var result = await client.PublishAsync(appConfig.AppId, message);

                var version = result.TryGetProperty("versionNumber", out var v) ? v.GetInt32().ToString() : "?";
                var summary = result.TryGetProperty("summary", out var s) ? s.GetString() : message;

                AnsiConsole.MarkupLine($"[green]✓[/] Version [bold]{version}[/] saved{(summary != null ? $": {summary}" : "")}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        }, messageOption);

        return cmd;
    }
}
