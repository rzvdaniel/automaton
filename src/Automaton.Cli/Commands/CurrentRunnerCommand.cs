using Automaton.Cli.Models;
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
            var checks = new List<CommandDiagnosticCheck>();
            var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.TryLoadProjectConfig(cwd);
                var resolved = config.ResolveContext(appConfig, cwd);
                var server = resolved.Server;
                var apiKey = resolved.ApiKey;
                var appId = resolved.AppId;
                var appLinkCheck = CliCommandChecks.ValidateProjectLink(File.Exists(Path.Combine(cwd, "automaton.json")), appConfig, appId, nextSteps);
                var credentialsCheck = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
                checks.Add(appLinkCheck);
                checks.Add(credentialsCheck);

                if (!appLinkCheck.Success || !credentialsCheck.Success || appConfig == null || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                {
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Current runner lookup failed", Checks = checks, NextSteps = nextSteps.ToList() };
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

                var app = await client.GetAppAsync(appId!);
                var runnerId = app.TryGetProperty("executionRunnerId", out var runnerProperty) && runnerProperty.ValueKind == JsonValueKind.String
                    ? runnerProperty.GetString()
                    : null;
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RemoteAccess, true, $"remote app loaded: {appId}"));

                if (string.IsNullOrWhiteSpace(runnerId))
                {
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerAssignment, true, "no runner is currently assigned to this app"));
                    var noAssignment = new CommandDiagnosticResult { Success = true, Summary = "No runner assigned", Checks = checks, NextSteps = ["Run automaton deploy and choose a connected runner when prompted", "If no connected runner exists, create or deploy one in Automaton Studio, then rerun automaton deploy"] };
                    if (asJson)
                    {
                        CliJson.Write(new { success = true, summary = noAssignment.Summary, checks = noAssignment.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = noAssignment.NextSteps, appId = appId, runnerAssigned = false });
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
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerAssignment, true, $"runner assigned: {runnerId}"));
                var runnerResult = new CommandDiagnosticResult { Success = true, Summary = "Current runner loaded", Checks = checks, NextSteps = [] };

                if (asJson)
                {
                    CliJson.Write(new { success = true, summary = runnerResult.Summary, checks = runnerResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = runnerResult.NextSteps, appId = appId, runnerAssigned = true, runnerId, runnerName, status = connected ? "connected" : "offline" });
                    return;
                }

                AnsiConsole.MarkupLine($"[green]Assigned runner:[/] {runnerName ?? runnerId} ({runnerId})");
                AnsiConsole.MarkupLine(connected ? "[green]Status:[/] connected" : "[yellow]Status:[/] offline or unknown");
            }
            catch (Exception ex)
            {
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RemoteAccess, false, ex.Message));
                nextSteps.Add("Run automaton whoami to confirm the current server and identity");
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Current runner lookup failed", Checks = checks, NextSteps = nextSteps.ToList() };
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
