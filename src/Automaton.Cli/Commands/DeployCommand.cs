using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton deploy [--clean] [--skip-backend] [--publish]
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
        var publishOption = new Option<bool>("--publish", "Publish the app after a successful deploy");
        var openOption = new Option<bool>("--open", "Open the deployed app in the browser after success");
        var nonInteractiveOption = new Option<bool>("--non-interactive", "Disable prompts and rich terminal rendering");

        var cmd = new Command("deploy", "Build, upload, and deploy the app to Automaton")
        {
            runnerOption,
            cleanOption,
            skipBackendOption,
            dirOption,
            jsonOption,
            verboseOption,
            publishOption,
            openOption,
            nonInteractiveOption
        };

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var runner = context.ParseResult.GetValueForOption(runnerOption);
            var clean = context.ParseResult.GetValueForOption(cleanOption);
            var skipBackend = context.ParseResult.GetValueForOption(skipBackendOption);
            var dir = context.ParseResult.GetValueForOption(dirOption);
            var asJson = context.ParseResult.GetValueForOption(jsonOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var publish = context.ParseResult.GetValueForOption(publishOption);
            var open = context.ParseResult.GetValueForOption(openOption);
            var nonInteractiveFlag = context.ParseResult.GetValueForOption(nonInteractiveOption);

            var preparationService = new DeployPreparationService(config);
            var publishService = new PublishService(config);
            var nonInteractive = CliInteraction.IsNonInteractive(nonInteractiveFlag, asJson);
            var headingWritten = false;
            var deployService = new DeployService(
                config,
                message => WriteProgress(message, asJson, nonInteractive),
                check => WriteCheck(check, asJson, nonInteractive));
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var projectConfig = config.TryLoadProjectConfig(cwd);
                var preparation = skipBackend
                    ? new DeployPreparationResult { Success = true }
                    : await preparationService.PrepareAsync(cwd, runner);

                if (preparation.RequiresRunnerSelection && !nonInteractive && string.IsNullOrWhiteSpace(runner) && preparation.AvailableRunners.Count > 1)
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

                    WriteResult(failure, asJson, verbose, nonInteractive);
                    Environment.ExitCode = 1;
                    return;
                }

                WriteDeployHeading(projectConfig?.Name, projectConfig?.AppId, asJson, nonInteractive, ref headingWritten);
                var result = await deployService.DeployAsync(cwd, clean, skipBackend, dir);
                result = MergePreparation(result, preparation);

                if (result.Success)
                {
                    if (publish)
                    {
                        var publishResult = await publishService.PublishAsync(cwd, slug: null, requiresAuthentication: null, isForkable: null, yes: true);
                        if (asJson)
                        {
                            WriteCombinedJson(result, publishResult);
                        }
                        else
                        {
                            WriteResult(result, asJson, verbose, nonInteractive);
                            PublishCommand.WriteResult(publishResult, asJson, nonInteractive);
                        }
                        if (!publishResult.Success)
                        {
                            Environment.ExitCode = 1;
                            return;
                        }

                        if (open)
                        {
                            CliBrowserService.OpenUrl(publishResult.Url!);
                        }
                    }
                    else
                    {
                        result = AppendDefaultSuccessNextSteps(result);
                        WriteResult(result, asJson, verbose, nonInteractive);

                        if (open)
                        {
                            var (client, server) = CreateApiClient(config, cwd);
                            using (client)
                            {
                            var launchCode = await client.CreatePrivateLaunchAsync(result.AppId!);
                            CliBrowserService.OpenUrl(CliAppUrlBuilder.BuildPrivateLaunchUrl(server, result.AppId, launchCode));
                            }
                        }
                    }
                }
                else
                {
                    WriteResult(result, asJson, verbose, nonInteractive);
                }

                Environment.ExitCode = result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.ExitCode = 1;
            }
        });

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
            HasWarnings = result.HasWarnings || preparation.Checks.Any(check => !check.Success && check.IsWarning),
            AppId = result.AppId,
            AppName = result.AppName,
            OpenAppUrl = result.OpenAppUrl,
            StudioAppUrl = result.StudioAppUrl,
            DeployDirectory = result.DeployDirectory,
            Clean = result.Clean,
            SkipBackend = result.SkipBackend,
            Checks = preparation.Checks.Concat(result.Checks).ToList(),
            NextSteps = preparation.NextSteps.Concat(result.NextSteps).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SourceUploaded = result.SourceUploaded,
            Uploaded = result.Uploaded,
            BackendUploaded = result.BackendUploaded,
            Backend = result.Backend,
            BackendRunning = result.BackendRunning
        };
    }

    private static DeployResult AppendDefaultSuccessNextSteps(DeployResult result)
    {
        if (!result.Success)
        {
            return result;
        }

        var nextSteps = result.NextSteps.ToList();
        AddNextStep(nextSteps, "Run automaton publish --yes --slug <your-app-slug> when you want to make this deploy public");
        AddNextStep(nextSteps, "Or use automaton deploy --publish on the next deploy to deploy and publish in one command");

        return new DeployResult
        {
            Success = result.Success,
            Summary = result.Summary,
            AppId = result.AppId,
            AppName = result.AppName,
            OpenAppUrl = result.OpenAppUrl,
            StudioAppUrl = result.StudioAppUrl,
            DeployDirectory = result.DeployDirectory,
            Clean = result.Clean,
            SkipBackend = result.SkipBackend,
            Checks = result.Checks,
            NextSteps = nextSteps,
            SourceUploaded = result.SourceUploaded,
            Uploaded = result.Uploaded,
            BackendUploaded = result.BackendUploaded,
            Backend = result.Backend,
            HasWarnings = result.HasWarnings,
            BackendRunning = result.BackendRunning
        };
    }

    private static void AddNextStep(ICollection<string> nextSteps, string value)
    {
        if (nextSteps.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        nextSteps.Add(value);
    }

    private static void WriteResult(DeployResult result, bool asJson, bool verbose, bool nonInteractive)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = result.Success,
                summary = result.Summary,
                hasWarnings = result.HasWarnings,
                appId = result.AppId,
                appName = result.AppName,
                openAppUrl = result.OpenAppUrl,
                studioAppUrl = result.StudioAppUrl,
                deployDirectory = result.DeployDirectory,
                clean = result.Clean,
                skipBackend = result.SkipBackend,
                checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                nextSteps = result.NextSteps,
                sourceUploaded = result.SourceUploaded,
                uploaded = result.Uploaded,
                backendUploaded = result.BackendUploaded,
                backend = result.Backend,
                backendRunning = result.BackendRunning
            });
            return;
        }

        if (nonInteractive)
        {
            WritePlainResult(result, verbose);
            return;
        }

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
            if (!string.IsNullOrWhiteSpace(result.OpenAppUrl))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(result.HasWarnings
                    ? "[yellow bold]Deploy complete with warnings.[/]"
                    : "[green bold]Deploy complete.[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[bold]Private app:[/] {Markup.Escape(result.OpenAppUrl)}");
                AnsiConsole.MarkupLine("[bold]Open now:[/] automaton open");
                if (!string.IsNullOrWhiteSpace(result.StudioAppUrl))
                {
                    AnsiConsole.MarkupLine($"[bold]Manage in Studio:[/] {Markup.Escape(result.StudioAppUrl)}");
                }
            }
            else
            {
                AnsiConsole.MarkupLine(result.HasWarnings
                    ? "[yellow bold]Deploy complete with warnings.[/]"
                    : "[green bold]Deploy complete.[/]");
            }
        }
    }

    private static void WriteProgress(string message, bool asJson, bool nonInteractive)
    {
        if (asJson || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (nonInteractive)
        {
            Console.WriteLine(message);
            return;
        }

        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]");
    }

    private static void WriteCheck(DeployCheckResult check, bool asJson, bool nonInteractive)
    {
        if (asJson)
        {
            return;
        }

        var status = check.Success
            ? "pass"
            : check.IsWarning ? "warn" : "fail";

        if (nonInteractive)
        {
            Console.WriteLine($"Stage: {check.Name} | {status} | {check.Message}");
            return;
        }

        var markupStatus = check.Success
            ? "[green]pass[/]"
            : check.IsWarning ? "[yellow]warn[/]" : "[red]fail[/]";
        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(check.Name)}[/] {markupStatus} {Markup.Escape(check.Message)}");
    }

    private static void WriteDeployHeading(string? appName, string? appId, bool asJson, bool nonInteractive, ref bool headingWritten)
    {
        if (asJson || headingWritten || string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(appId))
        {
            return;
        }

        if (nonInteractive)
        {
            Console.WriteLine($"Deploying {appName} ({appId})");
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold]Deploying[/] [cyan]{appName}[/] ({appId})");
        }

        headingWritten = true;
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

    private static void WritePlainResult(DeployResult result, bool verbose)
    {
        WritePlainSkippedSection("Source skipped files", result.SourceUploaded, verbose);
        WritePlainSkippedSection("Frontend skipped files", result.Uploaded, verbose);
        WritePlainSkippedSection("Backend skipped files", result.BackendUploaded, verbose);

        if (result.NextSteps.Count > 0)
        {
            Console.WriteLine("Next steps:");
            foreach (var nextStep in result.NextSteps)
            {
                Console.WriteLine($" - {nextStep}");
            }
        }

        if (result.Success)
        {
            if (!string.IsNullOrWhiteSpace(result.OpenAppUrl))
            {
                Console.WriteLine(result.HasWarnings ? "Deploy complete with warnings." : "Deploy complete.");
                Console.WriteLine();
                Console.WriteLine($"Private app: {result.OpenAppUrl}");
                Console.WriteLine("Open now: automaton open");
                if (!string.IsNullOrWhiteSpace(result.StudioAppUrl))
                {
                    Console.WriteLine($"Manage in Studio: {result.StudioAppUrl}");
                }
            }
            else
            {
                Console.WriteLine(result.HasWarnings ? "Deploy complete with warnings." : "Deploy complete.");
            }
        }
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

    private static void WritePlainSkippedSection(string title, DeployUploadResult? uploadResult, bool verbose)
    {
        if (!verbose || uploadResult?.SkippedEntries == null || uploadResult.SkippedEntries.Count == 0)
        {
            return;
        }

        Console.WriteLine(title);
        foreach (var entry in uploadResult.SkippedEntries)
        {
            Console.WriteLine($" - {entry.Path} [{entry.RuleSource}] {entry.Reason}");
        }
    }

    private static void WriteCombinedJson(DeployResult deployResult, PublishResult publishResult)
    {
        CliJson.Write(new
        {
            success = deployResult.Success && publishResult.Success,
            summary = publishResult.Success ? "Deploy and publish complete" : "Deploy complete, publish failed",
            deployed = deployResult.Success,
            published = publishResult.Success,
            hasWarnings = deployResult.HasWarnings,
            appId = deployResult.AppId,
            appName = deployResult.AppName,
            openAppUrl = deployResult.OpenAppUrl,
            studioAppUrl = deployResult.StudioAppUrl,
            deployDirectory = deployResult.DeployDirectory,
            clean = deployResult.Clean,
            skipBackend = deployResult.SkipBackend,
            checks = deployResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
            nextSteps = publishResult.Success
                ? deployResult.NextSteps
                : deployResult.NextSteps.Concat(publishResult.NextSteps).Distinct(StringComparer.OrdinalIgnoreCase),
            sourceUploaded = deployResult.SourceUploaded,
            uploaded = deployResult.Uploaded,
            backendUploaded = deployResult.BackendUploaded,
            backend = deployResult.Backend,
            backendRunning = deployResult.BackendRunning,
            url = publishResult.Url,
            slug = publishResult.Slug,
            publishChecks = publishResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message })
        });
    }

    private static (AutomatonApiClient Client, string Server) CreateApiClient(ConfigService config, string cwd)
    {
        var appConfig = config.TryLoadProjectConfig(cwd);
        var (server, apiKey) = config.ResolveCredentials(appConfig, cwd);
        if (appConfig == null || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Could not resolve app credentials for open.");
        }

        return (new AutomatonApiClient(server!, apiKey!), server!);
    }
}
