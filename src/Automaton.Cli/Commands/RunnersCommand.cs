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
        var cmd = new Command("runners", "List runners available on your Automaton account");

        cmd.SetHandler(async () =>
        {
            try
            {
                var (server, apiKey) = config.RequireCredentials();
                using var client = new AutomatonApiClient(server, apiKey);
                var result = await client.ListRunnersAsync();

                var runners = new List<(string Name, string Status)>();
                if (result.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in result.EnumerateArray())
                    {
                        var rname = r.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
                        var connected = r.TryGetProperty("connectionId", out var c) &&
                                        c.ValueKind != JsonValueKind.Null &&
                                        !string.IsNullOrEmpty(c.GetString());
                        runners.Add((rname, connected ? "connected" : "offline"));
                    }
                }

                if (runners.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No runners registered.[/] Start a runner and register it first.");
                    return;
                }

                var table = new Table();
                table.AddColumn("Name");
                table.AddColumn("Status");

                foreach (var (rname, status) in runners)
                {
                    var statusMarkup = status == "connected"
                        ? "[green]● connected[/]"
                        : "[grey]○ offline[/]";
                    table.AddRow(rname, statusMarkup);
                }

                AnsiConsole.Write(table);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        });

        return cmd;
    }
}
