using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton deploy [--clean] [--skip-backend]
///
/// Workflow:
///   1. Read automaton.json for appId + frontend/backend deploy config
///   2. Run frontend.build if configured
///   3. Zip frontend.deployDir (or --dir override)
///   4. Upload frontend runtime to the server
///   5. Upload backend runtime and deploy backend scripts (unless --skip-backend)
/// </summary>
public static class DeployCommand
{
    public static Command Build(ConfigService config)
    {
        var runnerOption = new Option<string?>("--runner", "Runner ID or exact runner name to assign before backend deploy when needed");
        var cleanOption = new Option<bool>(
            "--clean",
            "Wipe all server-side app files before uploading (full replace)");

        var skipBackendOption = new Option<bool>(
            "--skip-backend",
            "Skip deploying backend Python scripts after upload");

        var dirOption = new Option<string?>(
            ["--dir", "-d"],
            "Frontend directory to upload (overrides frontend.deployDir in automaton.json)");

        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var verboseOption = new Option<bool>("--verbose", "Print skipped-file details and exclusion reasons");

        var cmd = new Command("deploy", "Build, upload, and deploy the app to Automaton")
        {
            runnerOption,
            cleanOption,
            skipBackendOption,
            dirOption,
            jsonOption,
            verboseOption
        };

        cmd.SetHandler(async (string? runner, bool clean, bool skipBackend, string? dir, bool asJson, bool verbose) =>
        {
            var deployService = new DeployService(config);
            var preparationService = new DeployPreparationService(config);

            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var preparation = skipBackend
                    ? new DeployPreparationResult { Success = true }
                    : await preparationService.PrepareAsync(cwd, runner);

                if (!preparation.Success && !asJson && string.IsNullOrWhiteSpace(runner) && preparation.AvailableRunners.Count > 1)
                {
                    var prompt = new SelectionPrompt<RunnerSelectionItem>()
                        .Title("Select a connected runner for backend deploy")
                        .UseConverter(item => $"{item.Name} ({item.Id})");

                    prompt.AddChoices(preparation.AvailableRunners);
                    var selected = AnsiConsole.Prompt(prompt);
                    preparation = await preparationService.PrepareAsync(cwd, selected.Id);
                }

                if (!preparation.Success)
                {
                    var failure = new DeployResult
                    {
                        Success = false,
                        Summary = "Deploy failed",
                        Checks = preparation.Checks,
                        NextSteps = preparation.NextSteps
                    };

                    WriteResult(failure, asJson, verbose);
                    Environment.ExitCode = 1;
                    return;
                }

                var result = await deployService.DeployAsync(cwd, clean, skipBackend, dir);
                result = MergePreparation(result, preparation);

                WriteResult(result, asJson, verbose);
                Environment.ExitCode = result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.ExitCode = 1;
            }
        }, runnerOption, cleanOption, skipBackendOption, dirOption, jsonOption, verboseOption);

        return cmd;
    }

    private static DeployResult MergePreparation(DeployResult result, DeployPreparationResult preparation)
    {
        if (preparation.Checks.Count == 0 && preparation.NextSteps.Count == 0)
        {
            return result;
        }

        return new DeployResult
        {
            Success = result.Success,
            Summary = result.Summary,
            AppId = result.AppId,
            AppName = result.AppName,
            DeployDirectory = result.DeployDirectory,
            Clean = result.Clean,
            SkipBackend = result.SkipBackend,
            Checks = preparation.Checks.Concat(result.Checks).ToList(),
            NextSteps = preparation.NextSteps.Concat(result.NextSteps).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SourceUploaded = result.SourceUploaded,
            Uploaded = result.Uploaded,
            BackendUploaded = result.BackendUploaded,
            Backend = result.Backend
        };
    }

    private static void WriteResult(DeployResult result, bool asJson, bool verbose)
    {
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
                sourceUploaded = result.SourceUploaded,
                uploaded = result.Uploaded,
                backendUploaded = result.BackendUploaded,
                backend = result.Backend
            });
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
        WriteVerboseSkippedEntries(result, verbose);

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
    }

    private static void WriteVerboseSkippedEntries(DeployResult result, bool verbose)
    {
        if (!verbose)
        {
            return;
        }

        WriteSkippedSection("Source skipped files", result.SourceUploaded);
        WriteSkippedSection("Frontend skipped files", result.Uploaded);
        WriteSkippedSection("Backend skipped files", result.BackendUploaded);
    }

    private static void WriteSkippedSection(string title, DeployUploadResult? uploadResult)
    {
        if (uploadResult?.SkippedEntries == null || uploadResult.SkippedEntries.Count == 0)
        {
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]{title}[/]");
        foreach (var entry in uploadResult.SkippedEntries)
        {
            var path = Markup.Escape(entry.Path);
            var ruleSource = Markup.Escape(entry.RuleSource);
            var reason = Markup.Escape(entry.Reason);
            AnsiConsole.MarkupLine($" - [grey]{path}[/] [[{ruleSource}]] {reason}");
        }
    }
}
