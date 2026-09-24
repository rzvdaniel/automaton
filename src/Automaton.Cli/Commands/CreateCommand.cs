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
        var environmentOption = new Option<string?>("--env", "Local project profile to link, for example local or live");

        var cmd = new Command("create", "Create a remote Automaton app for the current folder and link the selected local profile")
        {
            nameOption,
            jsonOption,
            environmentOption
        };

        cmd.SetHandler(
            (string? name, bool asJson, string? environmentName) =>
                HandleCreateAsync(config, name, asJson, environmentName),
            nameOption,
            jsonOption,
            environmentOption);

        return cmd;
    }

    private static async Task HandleCreateAsync(
        ConfigService config,
        string? name,
        bool asJson,
        string? environmentName)
    {
        var cwd = Directory.GetCurrentDirectory();
        var (server, apiKey) = config.ResolveCredentials(
            directory: cwd,
            environmentName: environmentName,
            allowMissingEnvironment: true);
        name = ResolveName(config, cwd, name, asJson);

        var result = await new NewAppService(config).CreateAsync(cwd, name, server, apiKey, environmentName);
        WriteResult(result, asJson);
    }

    private static string? ResolveName(ConfigService config, string cwd, string? name, bool asJson)
    {
        var existingConfig = config.TryLoadProjectConfig(cwd);
        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(existingConfig?.Name))
            name = existingConfig.Name;

        if (!asJson && string.IsNullOrWhiteSpace(name))
        {
            Console.Write("App name: ");
            name = Console.ReadLine()?.Trim();
        }

        return name;
    }

    private static void WriteResult(NewAppResult result, bool asJson)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = result.Success,
                summary = result.Success ? "Create complete" : result.Summary,
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
                Success = false,
                Summary = "Create failed",
                Checks = result.Checks,
                NextSteps = result.NextSteps
            });
            Environment.ExitCode = 1;
            return;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Created app [bold]{result.Name}[/]");
        AnsiConsole.MarkupLine($"[grey]App ID:[/] {result.AppId}");
        AnsiConsole.MarkupLine("[grey]automaton.json written.[/]");
        Environment.ExitCode = 0;
    }
}
