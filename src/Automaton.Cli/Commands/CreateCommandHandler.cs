using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

internal static class CreateCommandHandler
{
    public static async Task HandleAsync(
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
        var existingConfig = config.TryLoadProjectConfig(cwd);
        name = ResolveName(name, existingConfig?.Name, asJson);

        var result = await new NewAppService(config).CreateAsync(
            cwd, name, server, apiKey, environmentName);
        WriteResult(result, asJson);
    }

    private static string? ResolveName(string? name, string? configuredName, bool asJson)
    {
        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(configuredName))
        {
            name = configuredName;
        }

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
            WriteJsonResult(result);
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

    private static void WriteJsonResult(NewAppResult result)
    {
        CliJson.Write(new
        {
            success = result.Success,
            summary = result.Success ? "Create complete" : result.Summary,
            checks = result.Checks.Select(check => new
            {
                name = check.Name,
                success = check.Success,
                warning = check.IsWarning,
                error = check.Message
            }),
            nextSteps = result.NextSteps,
            appId = result.AppId,
            name = result.Name,
            server = result.Server,
            configPath = result.ConfigPath
        });
        Environment.ExitCode = result.Success ? 0 : 1;
    }
}
