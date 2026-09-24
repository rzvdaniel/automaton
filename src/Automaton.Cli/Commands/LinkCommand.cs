using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class LinkCommand
{
    public static Command Build(ConfigService config)
    {
        var appIdArgument = new Argument<string?>("app-id-or-slug", () => null, "Existing Automaton app ID or exact app slug to link to this directory")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = new Option<string?>("--env", "Local project profile to link, for example local or live");

        var cmd = new Command("link", "Link this directory to an existing Automaton app")
        {
            appIdArgument,
            jsonOption,
            environmentOption
        };

        cmd.SetHandler(
            (string? appId, bool asJson, string? environmentName) =>
                HandleLinkAsync(config, appId, asJson, environmentName),
            appIdArgument,
            jsonOption,
            environmentOption);

        return cmd;
    }

    private static async Task HandleLinkAsync(
        ConfigService config,
        string? appId,
        bool asJson,
        string? environmentName)
    {
        var cwd = Directory.GetCurrentDirectory();
        var (server, apiKey) = config.ResolveCredentials(
            directory: cwd,
            environmentName: environmentName,
            allowMissingEnvironment: true);
        var resolvedAppId = ResolveAppId(appId, asJson);
        var result = await new LinkAppService(config).LinkAsync(
            cwd, resolvedAppId, server, apiKey, environmentName);

        WriteResult(result, asJson);
    }

    private static string? ResolveAppId(string? appId, bool asJson)
    {
        if (asJson || !string.IsNullOrWhiteSpace(appId))
            return appId;

        Console.Write("App ID: ");
        return Console.ReadLine()?.Trim();
    }

    private static void WriteResult(LinkAppResult result, bool asJson)
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

        AnsiConsole.MarkupLine($"[green]✓[/] Linked app [bold]{result.Name}[/] ({result.AppId})");
        AnsiConsole.MarkupLine("[grey]Existing local source was not modified. Run 'automaton deploy' to deploy it.[/]");
        Environment.ExitCode = 0;
    }
}
