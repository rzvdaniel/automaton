using Automaton.Cli.Models;
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
            var checks = new List<CommandDiagnosticCheck>();
            var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var (server, apiKey) = config.ResolveCredentials(directory: cwd);
                var credentialsCheck = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
                checks.Add(credentialsCheck);

                if (!credentialsCheck.Success || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                {
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Runner listing failed", Checks = checks, NextSteps = nextSteps.ToList() };
                    if (asJson)
                    {
                        CliJson.Write(new { success = false, summary = failure.Summary, checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = failure.NextSteps });
                    }
                    else
                    {
                        CliDiagnosticsWriter.Write(failure);
                    }
                    Environment.ExitCode = 1;
                    return;
                }

                using var client = new AutomatonApiClient(server!, apiKey!);
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

                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Runners, true, runners.Count == 0 ? "no runners registered" : $"{runners.Count} runner(s) loaded"));
                var runnersResult = new CommandDiagnosticResult { Success = true, Summary = runners.Count == 0 ? "No runners registered" : "Runners loaded", Checks = checks, NextSteps = runners.Count == 0 ? ["Start and register a runner, or run automaton ensure-local-runner"] : [] };

                if (asJson)
                {
                    CliJson.Write(new { success = true, summary = runnersResult.Summary, checks = runnersResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = runnersResult.NextSteps, server, runners });
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
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Runners, false, ex.Message));
                nextSteps.Add("Run automaton whoami to confirm the current server and identity");
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Runner listing failed", Checks = checks, NextSteps = nextSteps.ToList() };
                if (asJson)
                {
                    CliJson.Write(new { success = false, summary = failure.Summary, checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = failure.NextSteps });
                }
                else
                {
                    CliDiagnosticsWriter.Write(failure);
                }
                Environment.ExitCode = 1;
            }
        }, jsonOption);

        return cmd;
    }
}
