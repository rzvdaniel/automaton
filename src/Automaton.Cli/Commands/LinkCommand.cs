using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class LinkCommand
{
    public static Command Build(ConfigService config)
    {
        var appIdArgument = new Argument<string?>("app-id", () => null, "Existing Automaton app ID to link to this directory")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var environmentOption = new Option<string?>("--env", "Environment name to link, for example local or live");

        var cmd = new Command("link", "Link this directory to an existing Automaton app")
        {
            appIdArgument,
            jsonOption,
            environmentOption
        };

        cmd.SetHandler(async (string? appId, bool asJson, string? environmentName) =>
        {
            var cwd = Directory.GetCurrentDirectory();
            var (server, apiKey) = config.ResolveCredentials(directory: cwd, environmentName: environmentName);

            var resolvedAppId = appId;
            if (!asJson && string.IsNullOrWhiteSpace(resolvedAppId))
            {
                Console.Write("App ID: ");
                resolvedAppId = Console.ReadLine()?.Trim();
            }

            var linkService = new LinkAppService(config);
            var result = await linkService.LinkAsync(cwd, resolvedAppId, server, apiKey, environmentName);

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

            AnsiConsole.MarkupLine($"[green]✓[/] Linked to app [bold]{result.Name}[/] ({result.AppId})");
            AnsiConsole.MarkupLine("[grey]Run 'automaton pull' to download the app's current files.[/]");
            Environment.ExitCode = 0;
        }, appIdArgument, jsonOption, environmentOption);

        return cmd;
    }
}
