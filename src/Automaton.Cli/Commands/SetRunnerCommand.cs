using Automaton.Cli.Models;
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
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("set-runner", "Assign or clear the execution runner for the current app")
        {
            runnerArgument,
            clearOption,
            jsonOption,
        };

        cmd.SetHandler(async (string? runner, bool clear, bool asJson) =>
        {
            var checks = new List<CommandDiagnosticCheck>();
            var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!clear && string.IsNullOrWhiteSpace(runner))
                {
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerLookup, false, "provide a runner ID or exact runner name, or use --clear"));
                    nextSteps.Add("Run automaton runners to see available runners");
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Runner assignment failed", Checks = checks, NextSteps = nextSteps.ToList() };
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

                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.TryLoadProjectConfig(cwd);
                var (server, apiKey) = config.ResolveCredentials(appConfig, cwd);
                var appLinkCheck = CliCommandChecks.ValidateProjectLink(File.Exists(Path.Combine(cwd, "automaton.json")), appConfig, nextSteps);
                var credentialsCheck = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
                checks.Add(appLinkCheck);
                checks.Add(credentialsCheck);

                if (!appLinkCheck.Success || !credentialsCheck.Success || appConfig == null || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                {
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Runner assignment failed", Checks = checks, NextSteps = nextSteps.ToList() };
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

                if (clear)
                {
                    await client.UpdateAppRunnerAsync(appConfig.AppId, null);
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerAssignment, true, "runner assignment cleared"));
                    var clearResult = new CommandDiagnosticResult { Success = true, Summary = "Runner cleared", Checks = checks, NextSteps = [] };
                    if (asJson)
                    {
                        CliJson.Write(new { success = true, summary = clearResult.Summary, checks = clearResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = clearResult.NextSteps, action = "clear", appId = appConfig.AppId, runnerId = (string?)null });
                        return;
                    }
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
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerLookup, false, $"runner '{runner}' not found"));
                    nextSteps.Add("Run automaton runners to see available runners");
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Runner assignment failed", Checks = checks, NextSteps = nextSteps.ToList() };
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

                if (matches.Count > 1)
                {
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerLookup, false, $"multiple runners matched '{runner}'"));
                    nextSteps.Add("Use the runner ID instead of the runner name");
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Runner assignment failed", Checks = checks, NextSteps = nextSteps.ToList() };
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

                var selected = matches[0];
                var runnerId = selected.GetProperty("id").GetString();
                var runnerName = selected.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString() ?? runnerId : runnerId;

                await client.UpdateAppRunnerAsync(appConfig.AppId, runnerId);
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerLookup, true, $"runner resolved: {runnerName}"));
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerAssignment, true, $"runner assigned: {runnerId}"));
                var assignResult = new CommandDiagnosticResult { Success = true, Summary = "Runner assigned", Checks = checks, NextSteps = [] };
                if (asJson)
                {
                    CliJson.Write(new { success = true, summary = assignResult.Summary, checks = assignResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = assignResult.NextSteps, action = "assign", appId = appConfig.AppId, runnerId, runnerName });
                    return;
                }
                AnsiConsole.MarkupLine($"[green]✓[/] Assigned runner [bold]{runnerName}[/] to [cyan]{appConfig.Name}[/].");
            }
            catch (Exception ex)
            {
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.RunnerAssignment, false, ex.Message));
                nextSteps.Add("Run automaton whoami to confirm the current server and identity");
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Runner assignment failed", Checks = checks, NextSteps = nextSteps.ToList() };
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
        }, runnerArgument, clearOption, jsonOption);

        return cmd;
    }
}