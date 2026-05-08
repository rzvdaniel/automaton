using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class NewCommand
{
    public static Command Build(ConfigService config)
    {
        var nameArgument = new Argument<string?>("name", () => null, "App name")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var nameOption = new Option<string?>(["--name", "-n"], "App name");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("new", "Create a new Automaton app and link this directory")
        {
            nameArgument,
            nameOption,
            jsonOption
        };

        cmd.SetHandler(async (string? nameArgumentValue, string? nameOptionValue, bool asJson) =>
        {
            var cwd = Directory.GetCurrentDirectory();
            var (server, apiKey) = config.ResolveCredentials(directory: cwd);

            var resolvedName = string.IsNullOrWhiteSpace(nameOptionValue)
                ? nameArgumentValue
                : nameOptionValue;

            if (!asJson && string.IsNullOrWhiteSpace(resolvedName))
            {
                Console.Write("App name: ");
                resolvedName = Console.ReadLine()?.Trim();
            }

            var newAppService = new NewAppService(config);
            var result = await newAppService.CreateAsync(cwd, resolvedName, server, apiKey);

            if (asJson)
            {
                CliJson.Write(new
                {
                    success = result.Success,
                    summary = result.Summary,
                    checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                    nextSteps = result.NextSteps,
                    appId = result.AppId,
                    name = result.Name,
                    server = result.Server,
                    configPath = result.ConfigPath
                });
                Environment.ExitCode = result.Success ? 0 : 1;
                return;
            }

            if (!result.Success)
            {
                CliDiagnosticsWriter.Write(new CommandDiagnosticResult
                {
                    Success = result.Success,
                    Summary = result.Summary,
                    Checks = result.Checks,
                    NextSteps = result.NextSteps
                });
                Environment.ExitCode = 1;
                return;
            }

            AnsiConsole.MarkupLine($"[green]✓[/] Created app [bold]{result.Name}[/]");
            AnsiConsole.MarkupLine($"[grey]App ID:[/] {result.AppId}");
            AnsiConsole.MarkupLine("[grey]automaton.json written.[/]");
            AnsiConsole.MarkupLine("[grey]Run 'automaton deploy' when the app is ready to upload.[/]");
            Environment.ExitCode = 0;
        }, nameArgument, nameOption, jsonOption);

        return cmd;
    }
}