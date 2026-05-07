using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

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
            var checks = new List<CommandDiagnosticCheck>();
            var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var appConfig = config.TryLoadProjectConfig(cwd);
                var (server, apiKey) = config.ResolveCredentials(appConfig, cwd);
                var appLinkCheck = CliCommandChecks.ValidateProjectLink(File.Exists(Path.Combine(cwd, "automaton.json")), appConfig, nextSteps);
                var credentialsCheck = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
                checks.Add(appLinkCheck);
                checks.Add(credentialsCheck);

                if (!appLinkCheck.Success || !credentialsCheck.Success || appConfig == null || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                {
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Publish failed", Checks = checks, NextSteps = nextSteps.ToList() };
                    if (asJson)
                    {
                        CliJson.Write(new
                        {
                            success = failure.Success,
                            summary = failure.Summary,
                            checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                            nextSteps = failure.NextSteps,
                            appId = appConfig?.AppId
                        });
                    }
                    else
                    {
                        CliDiagnosticsWriter.Write(failure);
                    }
                    Environment.ExitCode = 1;
                    return;
                }

                if (!yes)
                {
                    Console.Write($"Publish the current server-side snapshot for '{appConfig.Name}'? [y/N]: ");
                    var confirmation = Console.ReadLine()?.Trim();
                    if (!string.Equals(confirmation, "y", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(confirmation, "yes", StringComparison.OrdinalIgnoreCase))
                    {
                        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.PublishApproval, false, "publish cancelled by user"));
                        var cancelled = new CommandDiagnosticResult { Success = false, Summary = "Publish cancelled", Checks = checks, NextSteps = ["Rerun automaton publish --yes when you are ready to snapshot the app"] };
                        if (asJson)
                        {
                            CliJson.Write(new { success = false, cancelled = true, summary = cancelled.Summary, checks = cancelled.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = cancelled.NextSteps, appId = appConfig.AppId });
                            return;
                        }

                        CliDiagnosticsWriter.Write(cancelled);
                        return;
                    }
                }

                if (!asJson)
                {
                    AnsiConsole.MarkupLine($"[bold]Publishing snapshot[/] for [cyan]{appConfig.Name}[/]...");
                }

                using var client = new AutomatonApiClient(server!, apiKey!);
                var result = await client.PublishAsync(appConfig.AppId, message);

                var version = result.TryGetProperty("versionNumber", out var v) ? v.GetInt32().ToString() : "?";
                var summary = result.TryGetProperty("summary", out var s) ? s.GetString() : message;
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Publish, true, $"version {version} saved"));
                var publishResult = new CommandDiagnosticResult { Success = true, Summary = "Publish complete", Checks = checks, NextSteps = [] };

                if (asJson)
                {
                    CliJson.Write(new { success = true, summary = publishResult.Summary, checks = publishResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = publishResult.NextSteps, appId = appConfig.AppId, version, publishSummary = summary });
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✓[/] Version [bold]{version}[/] saved{(summary != null ? $": {summary}" : "")}");
            }
            catch (Exception ex)
            {
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Publish, false, ex.Message));
                nextSteps.Add("Run automaton whoami to confirm the current server and identity");
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Publish failed", Checks = checks, NextSteps = nextSteps.ToList() };
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
        }, messageOption, yesOption, jsonOption);

        return cmd;
    }
}
