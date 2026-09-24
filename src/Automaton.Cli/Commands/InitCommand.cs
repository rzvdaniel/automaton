using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton init              — create a new app and write automaton.json
/// automaton init --app &lt;id-or-slug&gt; — link the current local project to an existing app
/// </summary>
public static class InitCommand
{
    public static Command Build(ConfigService config)
    {
        var appOption = new Option<string?>(
            ["--app", "-a"],
            "Link to an existing app ID or exact slug instead of creating a new one");

        var nameOption = new Option<string?>(
            ["--name", "-n"],
            "App name (required when creating a new app)");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("init", "Create a new Automaton app or link an existing one to this directory")
        {
            appOption,
            nameOption,
            jsonOption
        };

        cmd.SetHandler(
            (string? appId, string? name, bool asJson) => HandleInitAsync(config, appId, name, asJson),
            appOption,
            nameOption,
            jsonOption);

        return cmd;
    }

    private static async Task HandleInitAsync(
        ConfigService config,
        string? appId,
        string? name,
        bool asJson)
    {
        var cwd = Directory.GetCurrentDirectory();
        var (server, apiKey) = config.ResolveCredentials(directory: cwd, allowMissingEnvironment: true);
        if (!string.IsNullOrWhiteSpace(appId))
        {
            await LinkExistingAppAsync(config, cwd, appId, server, apiKey, asJson);
            return;
        }

        name = ResolveName(name, asJson);
        var result = await new NewAppService(config).CreateAsync(cwd, name, server, apiKey);
        WriteCreateResult(result, asJson);
    }

    private static string? ResolveName(string? name, bool asJson)
    {
        if (asJson || !string.IsNullOrWhiteSpace(name))
            return name;

        Console.Write("App name: ");
        return Console.ReadLine()?.Trim();
    }

    private static async Task LinkExistingAppAsync(
        ConfigService config,
        string cwd,
        string appId,
        string server,
        string apiKey,
        bool asJson)
    {
        var result = await new LinkAppService(config).LinkAsync(cwd, appId, server, apiKey);
        WriteLinkResult(result, asJson);
    }

    private static void WriteLinkResult(LinkAppResult result, bool asJson)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = result.Success,
                summary = result.Success ? "Init complete" : result.Summary,
                checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                nextSteps = result.NextSteps,
                action = "link",
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
            WriteFailure("Init failed", result.Checks, result.NextSteps);
            return;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Linked app [bold]{result.Name}[/] ({result.AppId})");
        AnsiConsole.MarkupLine("[grey]Existing local source was not modified. Run 'automaton deploy' to deploy it.[/]");
        Environment.ExitCode = 0;
    }

    private static void WriteCreateResult(NewAppResult result, bool asJson)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = result.Success,
                summary = result.Success ? "Init complete" : result.Summary,
                checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                nextSteps = result.NextSteps,
                action = "create",
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
            WriteFailure("Init failed", result.Checks, result.NextSteps);
            return;
        }

        AnsiConsole.MarkupLine($"[green]✓[/] Created app [bold]{result.Name}[/]");
        AnsiConsole.MarkupLine($"[grey]App ID:[/] {result.AppId}");
        AnsiConsole.MarkupLine("[grey]automaton.json written.[/]");
        Environment.ExitCode = 0;
    }

    private static void WriteFailure(
        string summary,
        IReadOnlyList<CommandDiagnosticCheck> checks,
        IReadOnlyList<string> nextSteps)
    {
        CliDiagnosticsWriter.Write(new CommandDiagnosticResult
        {
            Success = false,
            Summary = summary,
            Checks = checks,
            NextSteps = nextSteps
        });
        Environment.ExitCode = 1;
    }
}
