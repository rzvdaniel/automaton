using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

internal static class LinkCommandHandler
{
    internal static async Task HandleAsync(
        ConfigService config, string? appId, bool asJson, string? environmentName)
    {
        var directory = Directory.GetCurrentDirectory();
        var (server, apiKey) = config.ResolveCredentials(
            directory: directory,
            environmentName: environmentName,
            allowMissingEnvironment: true);
        var resolvedAppId = ResolveAppId(appId, asJson);
        var result = await new LinkAppService(config).LinkAsync(
            directory, resolvedAppId, server, apiKey, environmentName);
        WriteResult(result, asJson);
    }

    private static string? ResolveAppId(string? appId, bool asJson)
    {
        if (asJson || !string.IsNullOrWhiteSpace(appId)) return appId;
        Console.Write("App ID: ");
        return Console.ReadLine()?.Trim();
    }

    private static void WriteResult(LinkAppResult result, bool asJson)
    {
        if (asJson)
        {
            WriteJson(result);
            Environment.ExitCode = result.Success ? 0 : 1;
            return;
        }
        if (!result.Success)
        {
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
        AnsiConsole.MarkupLine($"[green]✓[/] Linked app [bold]{result.Name}[/] ({result.AppId})");
        AnsiConsole.MarkupLine("[grey]Existing local source was not modified. Run 'automaton deploy' to deploy it.[/]");
        Environment.ExitCode = 0;
    }

    private static void WriteJson(LinkAppResult result)
        => CliJson.Write(new
        {
            success = result.Success,
            summary = result.Summary,
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
}
