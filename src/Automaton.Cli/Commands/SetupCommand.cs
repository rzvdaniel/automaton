using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class SetupCommand
{
    public static Command Build(ConfigService config)
    {
        var serverOption = new Option<string?>("--server", "Automaton server URL (defaults to the hosted Automaton Cloud)");
        var keyOption = new Option<string?>(["--key", "-k"], "API key for authentication");
        var globalOption = new Option<bool>("--global", "Store credentials in ~/.automaton/config.json instead of the current project");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("setup", "Prepare this machine or workspace for Automaton development")
        {
            serverOption,
            keyOption,
            globalOption,
            jsonOption
        };

        cmd.SetHandler(async (string? server, string? key, bool global, bool asJson) =>
        {
            var cwd = Directory.GetCurrentDirectory();
            var existing = config.ResolveExistingConfig(global, cwd);

            var resolvedServer = string.IsNullOrWhiteSpace(server)
                ? existing.Server ?? SetupService.DefaultHostedServer
                : server;
            var resolvedKey = string.IsNullOrWhiteSpace(key)
                ? existing.ApiKey
                : key;

            if (!asJson && string.IsNullOrWhiteSpace(resolvedKey))
            {
                Console.Write("API key: ");
                resolvedKey = Console.ReadLine()?.Trim();
            }

            var setupService = new SetupService(config);
            var result = await setupService.SetupAsync(cwd, resolvedServer, resolvedKey, global);

            if (asJson)
            {
                CliJson.Write(new
                {
                    success = result.Success,
                    summary = result.Summary,
                    checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                    nextSteps = result.NextSteps,
                    server = result.Server,
                    storage = result.Storage,
                    user = result.Success ? new { id = result.UserId, name = result.UserName, email = result.UserEmail } : null
                });
                Environment.ExitCode = result.Success ? 0 : 1;
                return;
            }

            CliDiagnosticsWriter.Write(new CommandDiagnosticResult
            {
                Success = result.Success,
                Summary = result.Summary,
                Checks = result.Checks,
                NextSteps = result.NextSteps
            });

            if (result.Success)
            {
                var identity = result.UserName ?? result.UserEmail ?? result.UserId ?? "unknown";
                AnsiConsole.MarkupLine($"[green]✓[/] Hosted server: [bold]{result.Server}[/]");
                AnsiConsole.MarkupLine($"[green]✓[/] Authenticated as [bold]{identity}[/]");
                AnsiConsole.MarkupLine(global
                    ? "[grey]Credentials saved to ~/.automaton/config.json[/]"
                    : "[grey]Credentials saved to ./.automaton/config.json[/]");
            }

            Environment.ExitCode = result.Success ? 0 : 1;
        }, serverOption, keyOption, globalOption, jsonOption);

        return cmd;
    }
}