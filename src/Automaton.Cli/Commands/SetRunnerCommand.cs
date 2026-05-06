using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton set-runner &lt;runner-id-or-name&gt;  — assign an execution runner to the current app
/// automaton set-runner --clear             — clear the runner assignment
/// </summary>
public static class SetRunnerCommand
{
    public static Command Build(ConfigService config)
    {
        var runnerArgument = new Argument<string?>("runner", () => null, "Runner ID or exact runner name");
        var clearOption = new Option<bool>("--clear", "Clear the assigned runner for the current app");

        var cmd = new Command("set-runner", "Assign or clear the execution runner for the current app")
        {
            runnerArgument,
            clearOption,
        };

        cmd.SetHandler(async (string? runner, bool clear) =>
        {
            try
            {
                if (!clear && string.IsNullOrWhiteSpace(runner))
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Provide a runner ID or exact runner name, or use --clear.");
                    Environment.Exit(1);
                    return;
                }

                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.LoadProjectConfig(cwd);
                var (server, apiKey) = config.RequireCredentials(appConfig, cwd);

                using var client = new AutomatonApiClient(server, apiKey);

                if (clear)
                {
                    await client.UpdateAppRunnerAsync(appConfig.AppId, null);
                    AnsiConsole.MarkupLine($"[green]✓[/] Cleared runner assignment for [cyan]{appConfig.Name}[/].");
                    return;
                }

                var runnersResponse = await client.ListRunnersAsync();
                if (runnersResponse.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("Unexpected runners response from server.");
                }

                var matches = runnersResponse
                    .EnumerateArray()
                    .Where(entry =>
                    {
                        var id = entry.TryGetProperty("id", out var idProperty) ? idProperty.GetString() : null;
                        var name = entry.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() : null;
                        return string.Equals(id, runner, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(name, runner, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                if (matches.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] Runner '{runner}' not found. Run 'automaton runners' to see available runners.");
                    Environment.Exit(1);
                    return;
                }

                if (matches.Count > 1)
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] Multiple runners matched '{runner}'. Use the runner ID instead.");
                    Environment.Exit(1);
                    return;
                }

                var selected = matches[0];
                var runnerId = selected.GetProperty("id").GetString();
                var runnerName = selected.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? runnerId : runnerId;

                await client.UpdateAppRunnerAsync(appConfig.AppId, runnerId);
                AnsiConsole.MarkupLine($"[green]✓[/] Assigned runner [bold]{runnerName}[/] to [cyan]{appConfig.Name}[/].");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.Exit(1);
            }
        }, runnerArgument, clearOption);

        return cmd;
    }
}