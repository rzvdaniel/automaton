using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton deploy [--clean] [--skip-backend]
///
/// Workflow:
///   1. Read automaton.json for appId + optional build/deployDir
///   2. Run build command if configured
///   3. Zip resolved deploy directory
///   4. Upload zip to server
///   5. Deploy backend scripts (unless --skip-backend)
/// </summary>
public static class DeployCommand
{
    public static Command Build(ConfigService config)
    {
        var cleanOption = new Option<bool>(
            "--clean",
            "Wipe all server-side app files before uploading (full replace)");

        var skipBackendOption = new Option<bool>(
            "--skip-backend",
            "Skip deploying backend Python scripts after upload");

        var dirOption = new Option<string?>(
            ["--dir", "-d"],
            "Directory to upload (overrides deployDir in automaton.json)");

        var jsonOption = new Option<bool>("--json", "Print the result as JSON");

        var cmd = new Command("deploy", "Build, upload, and deploy the app to Automaton")
        {
            cleanOption,
            skipBackendOption,
            dirOption,
            jsonOption
        };

        cmd.SetHandler(async (bool clean, bool skipBackend, string? dir, bool asJson) =>
        {
            var deployService = new DeployService(config);

            try
            {
                var result = await deployService.DeployAsync(Directory.GetCurrentDirectory(), clean, skipBackend, dir);

                if (asJson)
                {
                    CliJson.Write(new
                    {
                        success = result.Success,
                        summary = result.Summary,
                        appId = result.AppId,
                        appName = result.AppName,
                        deployDirectory = result.DeployDirectory,
                        clean = result.Clean,
                        skipBackend = result.SkipBackend,
                        checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, error = check.Message }),
                        nextSteps = result.NextSteps,
                        uploaded = result.Uploaded,
                        backend = result.Backend
                    });
                    Environment.ExitCode = result.Success ? 0 : 1;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(result.AppName) && !string.IsNullOrWhiteSpace(result.AppId))
                {
                    AnsiConsole.MarkupLine($"[bold]Deploying[/] [cyan]{result.AppName}[/] ({result.AppId})");
                }

                var table = new Table();
                table.AddColumn("Stage");
                table.AddColumn("Result");
                table.AddColumn("Details");

                foreach (var check in result.Checks)
                {
                    table.AddRow(check.Name, check.Success ? "[green]pass[/]" : "[red]fail[/]", check.Message);
                }

                AnsiConsole.Write(table);

                if (result.NextSteps.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]Next steps:[/]");
                    foreach (var nextStep in result.NextSteps)
                    {
                        AnsiConsole.MarkupLine($" - {nextStep}");
                    }
                }

                if (result.Success)
                {
                    AnsiConsole.MarkupLine("[green bold]Deploy complete.[/]");
                }

                Environment.ExitCode = result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, cleanOption, skipBackendOption, dirOption, jsonOption);

        return cmd;
    }
}
