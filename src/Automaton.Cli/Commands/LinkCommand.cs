using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class LinkCommand
{
    public static Command Build(ConfigService config)
    {
        var appIdArgument = new Argument<string>("app-id", "Existing Automaton app ID to link to this directory");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("link", "Link this directory to an existing Automaton app")
        {
            appIdArgument,
            jsonOption
        };

        cmd.SetHandler(async (string appId, bool asJson) =>
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
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Link failed", Checks = checks, NextSteps = nextSteps.ToList() };
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

                AnsiConsole.MarkupLine($"[grey]Fetching app {appId}...[/]");
                var app = await client.GetAppAsync(appId);
                var appName = app.TryGetProperty("name", out var nameProperty) ? nameProperty.GetString()! : appId;
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, true, $"linked to app {appId}"));

                config.SaveProjectConfig(cwd, new AutomatonConfig { AppId = appId, Name = appName, Server = server });

                if (asJson)
                {
                    CliJson.Write(new { success = true, summary = "Link complete", checks = checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = Array.Empty<string>(), appId, name = appName, server, configPath = Path.Combine(cwd, "automaton.json") });
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✓[/] Linked to app [bold]{appName}[/] ({appId})");
                AnsiConsole.MarkupLine("[grey]Run 'automaton pull' to download the app's current files.[/]");
            }
            catch (Exception ex)
            {
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, false, ex.Message));
                nextSteps.Add("Run automaton whoami to confirm the current server and identity");
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Link failed", Checks = checks, NextSteps = nextSteps.ToList() };
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
        }, appIdArgument, jsonOption);

        return cmd;
    }
}