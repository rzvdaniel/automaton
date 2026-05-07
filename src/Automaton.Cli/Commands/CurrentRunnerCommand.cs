using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

public static class CurrentRunnerCommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("current-runner", "Show the runner currently assigned to the linked app")
        {
            jsonOption
        };

        cmd.SetHandler(async (bool asJson) =>
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.LoadProjectConfig(cwd);
                var (server, apiKey) = config.RequireCredentials(appConfig, cwd);
                using var client = new AutomatonApiClient(server, apiKey);

                var app = await client.GetAppAsync(appConfig.AppId);
                var runnerId = app.TryGetProperty("executionRunnerId", out var runnerProperty) && runnerProperty.ValueKind == JsonValueKind.String
                    ? runnerProperty.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(runnerId))
                {
                    if (asJson)
                    {
                        CliJson.Write(new { success = true, appId = appConfig.AppId, runnerAssigned = false });
                        return;
                    }

                    AnsiConsole.MarkupLine("[yellow]No runner is currently assigned to this app.[/]");
                    return;
                }

                var runners = await client.ListRunnersAsync();
                var selected = runners.ValueKind == JsonValueKind.Array
                    ? runners.EnumerateArray().FirstOrDefault(item => item.TryGetProperty("id", out var idProperty) && string.Equals(idProperty.GetString(), runnerId, StringComparison.OrdinalIgnoreCase))
                    : default;

                var runnerName = selected.ValueKind == JsonValueKind.Object && selected.TryGetProperty("name", out var nameProperty)
                    ? nameProperty.GetString()
                    : null;
                var connected = selected.ValueKind == JsonValueKind.Object
                    && selected.TryGetProperty("connectionId", out var connectionProperty)
                    && connectionProperty.ValueKind != JsonValueKind.Null
                    && !string.IsNullOrWhiteSpace(connectionProperty.GetString());

                if (asJson)
                {
                    CliJson.Write(new { success = true, appId = appConfig.AppId, runnerAssigned = true, runnerId, runnerName, status = connected ? "connected" : "offline" });
                    return;
                }

                AnsiConsole.MarkupLine($"[green]Assigned runner:[/] {runnerName ?? runnerId} ({runnerId})");
                AnsiConsole.MarkupLine(connected ? "[green]Status:[/] connected" : "[yellow]Status:[/] offline or unknown");
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