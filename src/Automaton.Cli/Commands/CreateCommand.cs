using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class CreateCommand
{
    public static Command Build(ConfigService config)
    {
        var nameOption = new Option<string?>(["--name", "-n"], "App name");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("create", "Create a new Automaton app and write automaton.json")
        {
            nameOption,
            jsonOption
        };

        cmd.SetHandler(async (string? name, bool asJson) =>
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
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Create failed", Checks = checks, NextSteps = nextSteps.ToList() };
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

                if (string.IsNullOrWhiteSpace(name))
                {
                    Console.Write("App name: ");
                    name = Console.ReadLine()?.Trim();
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, false, "app name is required"));
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Create failed", Checks = checks, NextSteps = ["Pass --name or answer the app name prompt"] };
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
                var (appId, appName) = await client.CreateAppAsync(name);
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, true, $"created app {appId}"));

                config.SaveProjectConfig(cwd, new AutomatonConfig
                {
                    AppId = appId,
                    Name = appName,
                    Server = server
                });

                if (asJson)
                {
                    CliJson.Write(new { success = true, appId, name = appName, server, configPath = Path.Combine(cwd, "automaton.json") });
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✓[/] Created app [bold]{appName}[/]");
                AnsiConsole.MarkupLine($"[grey]App ID:[/] {appId}");
                AnsiConsole.MarkupLine("[grey]automaton.json written.[/]");
            }
            catch (Exception ex)
            {
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, false, ex.Message));
                nextSteps.Add("Run automaton whoami to confirm the current server and identity");
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Create failed", Checks = checks, NextSteps = nextSteps.ToList() };
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
        }, nameOption, jsonOption);

        return cmd;
    }
}