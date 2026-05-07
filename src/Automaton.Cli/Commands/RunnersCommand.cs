using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton runners   — list connected runners
/// </summary>
public static class RunnersCommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("runners", "List runners available on your Automaton account")
        {
            jsonOption
        };

        cmd.SetHandler(async (bool asJson) =>
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var (server, apiKey) = config.RequireCredentials(directory: cwd);
                using var client = new AutomatonApiClient(server, apiKey);
                var result = await client.ListRunnersAsync();

                var runners = new List<object>();
                if (result.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in result.EnumerateArray())
                    {
                        var id = r.TryGetProperty("id", out var idProperty) ? idProperty.GetString() ?? "?" : "?";
                        var rname = r.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
                        var connected = r.TryGetProperty("connectionId", out var c) &&
                                        c.ValueKind != JsonValueKind.Null &&
                                        !string.IsNullOrEmpty(c.GetString());
                        runners.Add(new
                        {
                            id,
                            name = rname,
                            status = connected ? "connected" : "offline",
                            connectionId = connected ? c.GetString() : null
                        });
                    }
                }

                if (asJson)
                {
                    CliJson.Write(new { server, runners });
                    return;
                }

                if (runners.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No runners registered.[/] Start a runner and register it first.");
                    return;
                }

                var table = new Table();
                table.AddColumn("Runner");
                table.AddColumn("Status");

                foreach (var runner in runners)
                {
                    var status = runner.GetType().GetProperty("status")?.GetValue(runner)?.ToString() ?? "offline";
                    var label = $"{runner.GetType().GetProperty("name")?.GetValue(runner)} ({runner.GetType().GetProperty("id")?.GetValue(runner)})";
                    var statusMarkup = status == "connected"
                        ? "[green]● connected[/]"
                        : "[grey]○ offline[/]";
                    table.AddRow(label, statusMarkup);
                }

                AnsiConsole.Write(table);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        }, jsonOption);

        return cmd;
    }
}
