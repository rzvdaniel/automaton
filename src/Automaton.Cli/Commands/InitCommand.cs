using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton init              — create a new app and write automaton.json
/// automaton init --app &lt;id&gt;  — link the current directory to an existing app
/// </summary>
public static class InitCommand
{
    public static Command Build(ConfigService config)
    {
        var appOption = new Option<string?>(
            ["--app", "-a"],
            "Link to an existing app ID instead of creating a new one");

        var nameOption = new Option<string?>(
            ["--name", "-n"],
            "App name (required when creating a new app)");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("init", "Create a new Automaton app or link an existing one to this directory")
        {
            appOption,
            nameOption,
            jsonOption
        };

        cmd.SetHandler(async (string? appId, string? name, bool asJson) =>
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
                    var failure = new CommandDiagnosticResult { Success = false, Summary = "Init failed", Checks = checks, NextSteps = nextSteps.ToList() };
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

                if (!string.IsNullOrWhiteSpace(appId))
                {
                    // Link to existing app
                    AnsiConsole.MarkupLine($"[grey]Fetching app {appId}...[/]");
                    var app = await client.GetAppAsync(appId);
                    var appName = app.TryGetProperty("name", out var n) ? n.GetString()! : appId;
                    checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.LinkApp, true, $"linked to app {appId}"));

                    config.SaveProjectConfig(cwd, new AutomatonConfig { AppId = appId, Name = appName, Server = server });
                    if (asJson)
                    {
                        CliJson.Write(new { success = true, summary = "Init complete", checks = checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = Array.Empty<string>(), action = "link", appId, name = appName, server, configPath = Path.Combine(cwd, "automaton.json") });
                        return;
                    }
                    AnsiConsole.MarkupLine($"[green]✓[/] Linked to app [bold]{appName}[/] ({appId})");
                    AnsiConsole.MarkupLine("[grey]Run 'automaton pull' to download the app's current files.[/]");
                    return;
                }

                // Create new app
                if (string.IsNullOrWhiteSpace(name))
                {
                    Console.Write("App name: ");
                    name = Console.ReadLine()?.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, false, "app name is required"));
                        var failure = new CommandDiagnosticResult { Success = false, Summary = "Init failed", Checks = checks, NextSteps = ["Pass --name or answer the app name prompt"] };
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
                }

                AnsiConsole.MarkupLine($"[grey]Creating app '{name}'...[/]");
                var (newAppId, newAppName) = await client.CreateAppAsync(name);
                checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.CreateApp, true, $"created app {newAppId}"));

                config.SaveProjectConfig(cwd, new AutomatonConfig { AppId = newAppId, Name = newAppName, Server = server });

                if (asJson)
                {
                    CliJson.Write(new { success = true, summary = "Init complete", checks = checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = Array.Empty<string>(), action = "create", appId = newAppId, name = newAppName, server, configPath = Path.Combine(cwd, "automaton.json") });
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✓[/] Created app [bold]{newAppName}[/]");
                AnsiConsole.MarkupLine($"[grey]App ID:[/] {newAppId}");
                AnsiConsole.MarkupLine("[grey]automaton.json written.[/]");
            }
            catch (Exception ex)
            {
                checks.Add(new CommandDiagnosticCheck(!string.IsNullOrWhiteSpace(appId) ? CommandDiagnosticCheckNames.LinkApp : CommandDiagnosticCheckNames.CreateApp, false, ex.Message));
                nextSteps.Add("Run automaton whoami to confirm the current server and identity");
                var failure = new CommandDiagnosticResult { Success = false, Summary = "Init failed", Checks = checks, NextSteps = nextSteps.ToList() };
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
        }, appOption, nameOption, jsonOption);

        return cmd;
    }
}
