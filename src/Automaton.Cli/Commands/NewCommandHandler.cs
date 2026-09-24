using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

internal static class NewCommandHandler
{
    internal static async Task HandleAsync(
        ConfigService config, string? argumentName, string? optionName, bool asJson, string? environmentName)
    {
        var cwd = Directory.GetCurrentDirectory();
        var (server, apiKey) = config.ResolveCredentials(
            directory: cwd, environmentName: environmentName, allowMissingEnvironment: true);
        var name = ResolveName(argumentName, optionName, asJson);
        var result = await new NewAppService(config).CreateAsync(
            cwd, name, server, apiKey, environmentName);
        WriteResult(result, cwd, asJson);
    }

    private static string? ResolveName(string? argumentName, string? optionName, bool asJson)
    {
        var name = string.IsNullOrWhiteSpace(optionName) ? argumentName : optionName;
        if (!asJson && string.IsNullOrWhiteSpace(name))
        {
            Console.Write("App name: ");
            name = Console.ReadLine()?.Trim();
        }
        return name;
    }

    private static void WriteResult(NewAppResult result, string directory, bool asJson)
    {
        if (asJson)
        {
            WriteJson(result);
            Environment.ExitCode = result.Success ? 0 : 1;
            return;
        }
        if (!result.Success)
        {
            WriteFailure(result);
            Environment.ExitCode = 1;
            return;
        }
        WriteSuccess(result, directory);
        Environment.ExitCode = 0;
    }

    private static void WriteJson(NewAppResult result)
        => CliJson.Write(new
        {
            success = result.Success,
            summary = result.Summary,
            checks = MapChecks(result.Checks),
            nextSteps = result.NextSteps,
            appId = result.AppId,
            name = result.Name,
            server = result.Server,
            configPath = result.ConfigPath
        });

    private static IEnumerable<object> MapChecks(IEnumerable<CommandDiagnosticCheck> checks)
        => checks.Select(check => new
        {
            name = check.Name,
            success = check.Success,
            warning = check.IsWarning,
            error = check.Message
        });

    private static void WriteFailure(NewAppResult result)
    {
        if (HasLocalScaffoldFailure(result))
        {
            WriteRemoteFailure(result);
        }
        CliDiagnosticsWriter.Write(new CommandDiagnosticResult
        {
            Success = result.Success,
            Summary = result.Summary,
            Checks = result.Checks,
            NextSteps = result.NextSteps
        });
    }

    private static bool HasLocalScaffoldFailure(NewAppResult result)
        => result.Checks.Any(check =>
               check.Name == CommandDiagnosticCheckNames.LocalScaffold && check.Success) &&
           result.Checks.Any(check =>
               check.Name == CommandDiagnosticCheckNames.CreateApp && !check.Success);

    private static void WriteRemoteFailure(NewAppResult result)
    {
        var remoteFailure = result.Checks.Last(check =>
            check.Name == CommandDiagnosticCheckNames.CreateApp && !check.Success);
        AnsiConsole.MarkupLine("[yellow]Local scaffold created successfully.[/]");
        AnsiConsole.MarkupLine($"[red]Remote app creation failed:[/] {remoteFailure.Message}");
        AnsiConsole.MarkupLine("[grey]Your project is still usable locally.[/]");
        AnsiConsole.WriteLine();
    }

    private static void WriteSuccess(NewAppResult result, string directory)
    {
        AnsiConsole.MarkupLine($"[green]✓[/] Created app [bold]{result.Name}[/]");
        AnsiConsole.MarkupLine($"[grey]App ID:[/] {result.AppId}");
        AnsiConsole.MarkupLine($"[grey]Local project:[/] {Markup.Escape(directory)}");
        AnsiConsole.MarkupLine("[grey]automaton.json written. Build or edit the app locally, then run 'automaton deploy'.[/]");
    }
}
