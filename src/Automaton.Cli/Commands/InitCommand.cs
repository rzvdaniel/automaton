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
            var cwd = Directory.GetCurrentDirectory();
            var (server, apiKey) = config.ResolveCredentials(directory: cwd);

            if (!string.IsNullOrWhiteSpace(appId))
            {
                if (!asJson && string.IsNullOrWhiteSpace(appId))
                {
                    Console.Write("App ID: ");
                    appId = Console.ReadLine()?.Trim();
                }

                var linkService = new LinkAppService(config);
                var linkResult = await linkService.LinkAsync(cwd, appId, server, apiKey);

                if (asJson)
                {
                    CliJson.Write(new
                    {
                        success = linkResult.Success,
                        summary = linkResult.Success ? "Init complete" : linkResult.Summary,
                        checks = linkResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                        nextSteps = linkResult.NextSteps,
                        action = "link",
                        appId = linkResult.AppId,
                        name = linkResult.Name,
                        server = linkResult.Server,
                        configPath = linkResult.ConfigPath
                    });
                    Environment.ExitCode = linkResult.Success ? 0 : 1;
                    return;
                }

                if (!linkResult.Success)
                {
                    CliDiagnosticsWriter.Write(new CommandDiagnosticResult
                    {
                        Success = false,
                        Summary = "Init failed",
                        Checks = linkResult.Checks,
                        NextSteps = linkResult.NextSteps
                    });
                    Environment.ExitCode = 1;
                    return;
                }

                AnsiConsole.MarkupLine($"[green]✓[/] Linked to app [bold]{linkResult.Name}[/] ({linkResult.AppId})");
                AnsiConsole.MarkupLine("[grey]Run 'automaton pull' to download the app's current files.[/]");
                Environment.ExitCode = 0;
                return;
            }

            if (!asJson && string.IsNullOrWhiteSpace(name))
            {
                Console.Write("App name: ");
                name = Console.ReadLine()?.Trim();
            }

            var newAppService = new NewAppService(config);
            var createResult = await newAppService.CreateAsync(cwd, name, server, apiKey);

            if (asJson)
            {
                CliJson.Write(new
                {
                    success = createResult.Success,
                    summary = createResult.Success ? "Init complete" : createResult.Summary,
                    checks = createResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                    nextSteps = createResult.NextSteps,
                    action = "create",
                    appId = createResult.AppId,
                    name = createResult.Name,
                    server = createResult.Server,
                    configPath = createResult.ConfigPath
                });
                Environment.ExitCode = createResult.Success ? 0 : 1;
                return;
            }

            if (!createResult.Success)
            {
                CliDiagnosticsWriter.Write(new CommandDiagnosticResult
                {
                    Success = false,
                    Summary = "Init failed",
                    Checks = createResult.Checks,
                    NextSteps = createResult.NextSteps
                });
                Environment.ExitCode = 1;
                return;
            }

            AnsiConsole.MarkupLine($"[green]✓[/] Created app [bold]{createResult.Name}[/]");
            AnsiConsole.MarkupLine($"[grey]App ID:[/] {createResult.AppId}");
            AnsiConsole.MarkupLine("[grey]automaton.json written.[/]");
            Environment.ExitCode = 0;
        }, appOption, nameOption, jsonOption);

        return cmd;
    }
}
