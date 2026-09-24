using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

internal static class InitCommandHandler
{
    internal static async Task HandleAsync(ConfigService config, string? appId, string? name, bool asJson)
    {
        var cwd = Directory.GetCurrentDirectory();
        var (server, apiKey) = config.ResolveCredentials(directory: cwd, allowMissingEnvironment: true);
        if (!string.IsNullOrWhiteSpace(appId))
        {
            await LinkExistingAppAsync(config, cwd, appId, server, apiKey, asJson);
            return;
        }

        await CreateNewAppAsync(config, cwd, name, server, apiKey, asJson);
    }

    private static async Task LinkExistingAppAsync(
        ConfigService config, string cwd, string appId, string? server, string? apiKey, bool asJson)
    {
        if (!asJson && string.IsNullOrWhiteSpace(appId)) appId = PromptForAppId();
        var result = await new LinkAppService(config).LinkAsync(cwd, appId, server, apiKey);
        if (asJson)
        {
            WriteLinkJson(result);
            Environment.ExitCode = result.Success ? 0 : 1;
            return;
        }
        if (!result.Success)
        {
            WriteFailure("Init failed", result.Checks, result.NextSteps);
            Environment.ExitCode = 1;
            return;
        }
        AnsiConsole.MarkupLine($"[green]✓[/] Linked app [bold]{result.Name}[/] ({result.AppId})");
        AnsiConsole.MarkupLine("[grey]Existing local source was not modified. Run 'automaton deploy' to deploy it.[/]");
        Environment.ExitCode = 0;
    }

    private static async Task CreateNewAppAsync(
        ConfigService config, string cwd, string? name, string? server, string? apiKey, bool asJson)
    {
        if (!asJson && string.IsNullOrWhiteSpace(name)) name = PromptForAppName();
        var result = await new NewAppService(config).CreateAsync(cwd, name, server, apiKey);
        if (asJson)
        {
            WriteCreateJson(result);
            Environment.ExitCode = result.Success ? 0 : 1;
            return;
        }
        if (!result.Success)
        {
            WriteFailure("Init failed", result.Checks, result.NextSteps);
            Environment.ExitCode = 1;
            return;
        }
        AnsiConsole.MarkupLine($"[green]✓[/] Created app [bold]{result.Name}[/]");
        AnsiConsole.MarkupLine($"[grey]App ID:[/] {result.AppId}");
        AnsiConsole.MarkupLine("[grey]automaton.json written.[/]");
        Environment.ExitCode = 0;
    }

    private static string? PromptForAppId()
    {
        Console.Write("App ID: ");
        return Console.ReadLine()?.Trim();
    }

    private static string? PromptForAppName()
    {
        Console.Write("App name: ");
        return Console.ReadLine()?.Trim();
    }

    private static void WriteFailure(string summary, IReadOnlyList<CommandDiagnosticCheck> checks, IReadOnlyList<string> nextSteps)
        => CliDiagnosticsWriter.Write(new CommandDiagnosticResult
        {
            Success = false, Summary = summary, Checks = checks, NextSteps = nextSteps
        });

    private static void WriteLinkJson(LinkAppResult result)
        => CliJson.Write(new
        {
            success = result.Success, summary = result.Success ? "Init complete" : result.Summary,
            checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
            nextSteps = result.NextSteps, action = "link", appId = result.AppId, name = result.Name,
            server = result.Server, configPath = result.ConfigPath
        });

    private static void WriteCreateJson(NewAppResult result)
        => CliJson.Write(new
        {
            success = result.Success, summary = result.Success ? "Init complete" : result.Summary,
            checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
            nextSteps = result.NextSteps, action = "create", appId = result.AppId, name = result.Name,
            server = result.Server, configPath = result.ConfigPath
        });
}
