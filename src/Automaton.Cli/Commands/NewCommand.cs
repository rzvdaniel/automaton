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
        var environmentOption = new Option<string?>("--env", "Local project profile to link, for example local or live");

        var cmd = new Command("new", "Create a new Automaton app and link this directory")
        {
            nameArgument,
            nameOption,
            jsonOption,
            environmentOption
        };

        cmd.SetHandler(
            (string? nameArgumentValue, string? nameOptionValue, bool asJson, string? environmentName) =>
                HandleNewAsync(config, nameArgumentValue, nameOptionValue, asJson, environmentName),
            nameArgument,
            nameOption,
            jsonOption,
            environmentOption);

        return cmd;
    }

    private static async Task HandleNewAsync(
        ConfigService config,
        string? nameArgument,
        string? nameOption,
        bool asJson,
        string? environmentName)
    {
        var cwd = Directory.GetCurrentDirectory();
        var (server, apiKey) = config.ResolveCredentials(
            directory: cwd,
            environmentName: environmentName,
            allowMissingEnvironment: true);
        var name = ResolveName(nameArgument, nameOption, asJson);
        var result = await new NewAppService(config).CreateAsync(cwd, name, server, apiKey, environmentName);
        WriteResult(result, cwd, asJson);
    }

    private static string? ResolveName(string? nameArgument, string? nameOption, bool asJson)
    {
        var name = string.IsNullOrWhiteSpace(nameOption) ? nameArgument : nameOption;
        if (asJson || !string.IsNullOrWhiteSpace(name))
            return name;

        Console.Write("App name: ");
        return Console.ReadLine()?.Trim();
    }

    private static void WriteResult(NewAppResult result, string cwd, bool asJson)
    {
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
            WriteCreationFailureDetails(result);
            CliDiagnosticsWriter.Write(new CommandDiagnosticResult
            {
                Success = false,
                Summary = result.Summary,
                Checks = result.Checks,
                NextSteps = result.NextSteps
            });
            Environment.ExitCode = 1;
            return;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Created app [bold]{result.Name}[/]");
        AnsiConsole.MarkupLine($"[grey]App ID:[/] {result.AppId}");
        AnsiConsole.MarkupLine($"[grey]Local project:[/] {Spectre.Console.Markup.Escape(cwd)}");
        AnsiConsole.MarkupLine("[grey]automaton.json written. Build or edit the app locally, then run 'automaton deploy'.[/]");
        Environment.ExitCode = 0;
    }

    private static void WriteCreationFailureDetails(NewAppResult result)
    {
        if (!result.Checks.Any(check => check.Name == CommandDiagnosticCheckNames.LocalScaffold && check.Success)
            || !result.Checks.Any(check => check.Name == CommandDiagnosticCheckNames.CreateApp && !check.Success))
        {
            return;
        }

        var remoteFailure = result.Checks.Last(check =>
            check.Name == CommandDiagnosticCheckNames.CreateApp && !check.Success);
        AnsiConsole.MarkupLine("[yellow]Local scaffold created successfully.[/]");
        AnsiConsole.MarkupLine($"[red]Remote app creation failed:[/] {remoteFailure.Message}");
        AnsiConsole.MarkupLine("[grey]Your project is still usable locally.[/]");
        AnsiConsole.WriteLine();
    }
}
