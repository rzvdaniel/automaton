using System.Diagnostics;
using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.CommandLine.Invocation;

namespace Automaton.Cli.Commands;

/// <summary>
/// automaton deploy [--clean] [--publish]
///
/// Workflow:
///   1. Read automaton.json for appId + application contract
///   2. Validate the contract and package source plus the runtime artifact when applicable
///   3. Upload the package; the assigned Runner executes the configured build once
///   4. Deploy Actions and the configured runtime
/// </summary>
public static class DeployCommand
{
    private static readonly Stopwatch managedRunnerWaitTimer = new();
    private static int managedRunnerSpinnerFrame;
    private static DateTime managedRunnerLastNonInteractiveUpdateUtc;
    public static Command Build(ConfigService config)
    {
        var targetArgument = new Argument<string?>(
            "target",
            () => null,
            "Environment slug; omit to deploy the only environment or choose interactively")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var runnerOption = new Option<string?>("--runner", "Runner ID or exact runner name to assign before deployment when needed");
        var cleanOption = new Option<bool>(
            "--clean",
            "Wipe all server-side app files before uploading (full replace)");

        var dirOption = new Option<string?>(
            ["--dir", "-d"],
            "Application artifact directory to upload (overrides application.build.outputDirectory in automaton.json)");

        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var verboseOption = new Option<bool>("--verbose", "Print ignored-file details and exclusion reasons");
        var publishOption = new Option<bool>("--publish", "Publish the app after a successful deploy");
        var openOption = new Option<bool>("--open", "Open the deployed app in the browser after success");
        var nonInteractiveOption = new Option<bool>("--non-interactive", "Disable prompts and rich terminal rendering");
        var environmentOption = new Option<string?>("--env", "Use a named project environment from ./.automaton/environments/<name>.json");

        var cmd = new Command("deploy", "Build, upload, and deploy the app to Automaton")
        {
            targetArgument,
            runnerOption,
            cleanOption,
            dirOption,
            jsonOption,
            verboseOption,
            publishOption,
            openOption,
            nonInteractiveOption,
            environmentOption
        };

        cmd.SetHandler(async (InvocationContext context) =>
        {
            var targetSlug = context.ParseResult.GetValueForArgument(targetArgument);
            var runner = context.ParseResult.GetValueForOption(runnerOption);
            var clean = context.ParseResult.GetValueForOption(cleanOption);
            var dir = context.ParseResult.GetValueForOption(dirOption);
            var asJson = context.ParseResult.GetValueForOption(jsonOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var publish = context.ParseResult.GetValueForOption(publishOption);
            var open = context.ParseResult.GetValueForOption(openOption);
            var nonInteractiveFlag = context.ParseResult.GetValueForOption(nonInteractiveOption);
            var environmentName = context.ParseResult.GetValueForOption(environmentOption);

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
                var resolvedContext = config.ResolveContext(projectConfig, cwd, environmentName);
                var targetService = new DeploymentTargetService(config);
                var allTargets = (await targetService.ListAsync(cwd, environmentName, context.GetCancellationToken())).Targets;
                var target = !string.IsNullOrWhiteSpace(runner)
                    ? DeploymentTargetService.SelectEnvironment(allTargets, targetSlug, nonInteractive)
                    : DeploymentTargetService.SelectForDeploy(allTargets, targetSlug, nonInteractive);
                if (!string.IsNullOrWhiteSpace(runner) && target.Status == "Deploying")
                {
                    throw new InvalidOperationException($"Environment '{target.Slug}' is already Deploying and cannot be reassigned.");
                }
                if (!string.IsNullOrWhiteSpace(runner))
                {
                    target = await targetService.AssignRunnerAsync(
                        cwd,
                        target,
                        runner,
                        environmentName,
                        context.GetCancellationToken());
                }
                DeploymentTargetService.EnsureDeployable(target);
                if (!target.IsDefault && target.Runner == null)
                {
                    throw new InvalidOperationException(
                        $"Environment '{target.Slug}' has no assigned Runner. " +
                        $"Assign one with 'automaton deploy {target.Slug} --runner <runner-id-or-name>'.");
                }
                if (!target.IsDefault && (publish || open))
                {
                    throw new InvalidOperationException(
                        "--publish and --open apply only to the Live target.");
                }
                WriteDeployHeading(projectConfig?.Name, resolvedContext.AppId, resolvedContext.EnvironmentName, resolvedContext.Server, asJson, nonInteractive, ref headingWritten);
                var preparation = new DeployPreparationResult
                {
                    Success = true,
                    RunnerId = target.Runner?.Id,
                    RunnerName = target.Runner?.Name,
                    Checks = target.Runner == null
                        ? []
                        : [new DeployCheckResult(
                            DeployCheckNames.RunnerAssignment,
                            true,
                            $"environment '{target.Slug}' assigned to Runner {target.Runner.Name}")]
                };

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
                    context.ExitCode = 1;
                    return;
                }

                var result = await deployService.DeployAsync(
                    cwd,
                    clean,
                    dir,
                    environmentName,
                    target,
                    context.GetCancellationToken());
                if (result.Success)
                {
                    target = await targetService.ResolveAsync(
                        cwd,
                        target.Slug,
                        environmentName,
                        context.GetCancellationToken());
                    result = ApplyTargetIdentity(result, target);
                }
                result = MergePreparation(result, preparation);

                if (result.Success)
                {
                    if (publish)
                    {
                        var publishResult = await publishService.PublishAsync(cwd, slug: null, requiresAuthentication: null, isForkable: null, yes: true, environmentName: environmentName);
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
                            context.ExitCode = 1;
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
                            var (client, server) = CreateApiClient(config, cwd, environmentName);
                            using (client)
                            {
                            var launchCode = await client.CreateRuntimeLaunchAsync(result.AppId!);
                            CliBrowserService.OpenUrl(CliAppUrlBuilder.BuildRuntimeLaunchUrl(server, result.AppId, launchCode));
                            }
                        }
                    }
                }
                else
                {
                    WriteResult(result, asJson, verbose, nonInteractive);
                }

                context.ExitCode = result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                if (asJson)
                {
                    CliJson.Write(new
                    {
                        success = false,
                        summary = "Deploy failed",
                        error = ex.Message,
                        target = targetSlug
                    });
                }
                else if (nonInteractive)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                }
                else
                {
                    AnsiConsole.MarkupLine(
                        $"[red]Error:[/] {Markup.Escape(ex.Message)}");
                }
                context.ExitCode = 1;
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

        var automaticRunnerReady = result.Success && result.Runtime?.Success == true && result.Runtime.RunnerCount > 0;
        var preparationChecks = automaticRunnerReady
            ? preparation.Checks.Where(check => check.Name != DeployCheckNames.RunnerLookup || check.Success).ToList()
            : preparation.Checks;
        var preparationNextSteps = automaticRunnerReady
            ? preparation.NextSteps.Where(step => !step.Contains("runner", StringComparison.OrdinalIgnoreCase)).ToList()
            : preparation.NextSteps;

        return new DeployResult
        {
            Success = result.Success,
            Summary = result.Summary,
            HasWarnings = result.HasWarnings || preparationChecks.Any(check => !check.Success && check.IsWarning),
            AppId = result.AppId,
            AppName = result.AppName,
            TargetSlug = result.TargetSlug,
            TargetName = result.TargetName,
            RunnerName = result.RunnerName,
            DeploymentMode = result.DeploymentMode,
            OpenAppUrl = result.OpenAppUrl,
            StudioAppUrl = result.StudioAppUrl,
            DeployDirectory = result.DeployDirectory,
            Clean = result.Clean,
            Checks = preparationChecks.Concat(result.Checks).ToList(),
            NextSteps = preparationNextSteps.Concat(result.NextSteps).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SourceUploaded = result.SourceUploaded,
            RuntimeUploaded = result.RuntimeUploaded,
            Runtime = result.Runtime,
            RuntimeRunning = result.RuntimeRunning
        };
    }

    private static DeployResult ApplyTargetIdentity(
        DeployResult result,
        DeploymentTargetInfo target) =>
        new()
        {
            Success = result.Success,
            Summary = result.Summary,
            HasWarnings = result.HasWarnings,
            AppId = result.AppId,
            AppName = result.AppName,
            TargetSlug = target.Slug,
            TargetName = target.Name,
            RunnerName = target.Runner?.Name,
            DeploymentMode = target.IsDefault ? "default" : "named",
            OpenAppUrl = result.OpenAppUrl,
            StudioAppUrl = result.StudioAppUrl,
            DeployDirectory = result.DeployDirectory,
            Clean = result.Clean,
            Checks = result.Checks,
            NextSteps = result.NextSteps,
            SourceUploaded = result.SourceUploaded,
            RuntimeUploaded = result.RuntimeUploaded,
            Runtime = result.Runtime,
            RuntimeRunning = result.RuntimeRunning
        };

    private static DeployResult AppendDefaultSuccessNextSteps(DeployResult result)
    {
        if (!result.Success ||
            string.Equals(result.DeploymentMode, "named", StringComparison.Ordinal))
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
            TargetSlug = result.TargetSlug,
            TargetName = result.TargetName,
            RunnerName = result.RunnerName,
            DeploymentMode = result.DeploymentMode,
            OpenAppUrl = result.OpenAppUrl,
            StudioAppUrl = result.StudioAppUrl,
            DeployDirectory = result.DeployDirectory,
            Clean = result.Clean,
            Checks = result.Checks,
            NextSteps = nextSteps,
            SourceUploaded = result.SourceUploaded,
            RuntimeUploaded = result.RuntimeUploaded,
            Runtime = result.Runtime,
            HasWarnings = result.HasWarnings,
            RuntimeRunning = result.RuntimeRunning
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
                target = result.TargetSlug,
                targetName = result.TargetName,
                runner = result.RunnerName,
                deploymentMode = result.DeploymentMode,
                openAppUrl = result.OpenAppUrl,
                studioAppUrl = result.StudioAppUrl,
                deployDirectory = result.DeployDirectory,
                clean = result.Clean,
                checks = result.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                nextSteps = result.NextSteps,
                sourceUploaded = result.SourceUploaded,
                runtimeUploaded = result.RuntimeUploaded,
                runtime = result.Runtime,
                runtimeRunning = result.RuntimeRunning
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
            AnsiConsole.MarkupLine(
                $"[bold]Environment:[/] {Markup.Escape(result.TargetName ?? result.TargetSlug ?? "Live")} " +
                $"([grey]{Markup.Escape(result.DeploymentMode ?? "default")}[/])");
            AnsiConsole.MarkupLine(
                $"[bold]Runner:[/] {Markup.Escape(result.RunnerName ?? "managed/default assignment")}");
            if (!string.IsNullOrWhiteSpace(result.OpenAppUrl))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(result.HasWarnings
                    ? "[yellow bold]Deploy complete with warnings.[/]"
                    : "[green bold]Deploy complete.[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[bold]Open in Studio:[/] {Markup.Escape(result.OpenAppUrl)}");
                AnsiConsole.MarkupLine("[bold]Open now:[/] automaton open");
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
            if (message.Contains("Managed runtime is being prepared", StringComparison.OrdinalIgnoreCase))
            {
                managedRunnerWaitTimer.Restart();
                managedRunnerLastNonInteractiveUpdateUtc = DateTime.UtcNow;
                Console.WriteLine(message);
                return;
            }

            if (message.Contains("Waiting for managed runner", StringComparison.OrdinalIgnoreCase))
            {
                if (DateTime.UtcNow - managedRunnerLastNonInteractiveUpdateUtc >= TimeSpan.FromSeconds(15))
                {
                    managedRunnerLastNonInteractiveUpdateUtc = DateTime.UtcNow;
                    Console.WriteLine($"Still waiting for managed runner... {managedRunnerWaitTimer.Elapsed.TotalSeconds:F0}s");
                }
                return;
            }

            if (message.Contains("Managed runner connected", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Managed runner connected after {managedRunnerWaitTimer.Elapsed.TotalSeconds:F0}s.");
                managedRunnerWaitTimer.Stop();
                return;
            }

            Console.WriteLine(message);
            return;
        }

        if (message.Contains("Managed runtime is being prepared", StringComparison.OrdinalIgnoreCase))
        {
            managedRunnerWaitTimer.Restart();
            managedRunnerSpinnerFrame = 0;
            AnsiConsole.MarkupLine("[grey]Managed runtime is being prepared...[/]");
            return;
        }

        if (message.Contains("Waiting for managed runner", StringComparison.OrdinalIgnoreCase))
        {
            var unicodeSupported = Console.OutputEncoding.CodePage == 65001;
            var frames = unicodeSupported
                ? new[] { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' }
                : new[] { '|', '/', '-', '\\' };
            var frame = frames[managedRunnerSpinnerFrame++ % frames.Length];
            Console.Write($"\r{frame}");
            return;
        }

        if (message.Contains("Managed runner connected", StringComparison.OrdinalIgnoreCase))
        {
            Console.Write("\r\u001b[2K");
            AnsiConsole.MarkupLine($"[green]✓ Managed runner connected after {managedRunnerWaitTimer.Elapsed.TotalSeconds:F0}s.[/]");
            managedRunnerWaitTimer.Stop();
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

    private static void WriteDeployHeading(string? appName, string? appId, string? environmentName, string? server, bool asJson, bool nonInteractive, ref bool headingWritten)
    {
        if (asJson || headingWritten || string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(appId))
        {
            return;
        }

        if (nonInteractive)
        {
            Console.WriteLine($"Deploying {appName} ({appId})");
            Console.WriteLine($"Environment: {environmentName ?? "default"}");
            if (!string.IsNullOrWhiteSpace(server))
            {
                Console.WriteLine($"Server: {server}");
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold]Deploying[/] [cyan]{appName}[/] ({appId})");
            AnsiConsole.MarkupLine($"[bold]Environment:[/] {Markup.Escape(environmentName ?? "default")}");
            if (!string.IsNullOrWhiteSpace(server))
            {
                AnsiConsole.MarkupLine($"[bold]Server:[/] {Markup.Escape(server)}");
            }
        }

        headingWritten = true;
    }

    private static void WriteVerboseSkippedEntries(DeployResult result, bool verbose)
    {
        if (!verbose)
        {
            return;
        }

        WriteSkippedSection("Source ignored files", result.SourceUploaded);
        WriteSkippedSection("Runtime package ignored files", result.RuntimeUploaded);
    }

    private static void WritePlainResult(DeployResult result, bool verbose)
    {
        WritePlainSkippedSection("Source ignored files", result.SourceUploaded, verbose);
        WritePlainSkippedSection("Runtime package ignored files", result.RuntimeUploaded, verbose);

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
            Console.WriteLine(
                $"Environment: {result.TargetName ?? result.TargetSlug ?? "Live"} ({result.DeploymentMode ?? "Live"})");
            Console.WriteLine(
                $"Runner: {result.RunnerName ?? "managed/default assignment"}");
            if (!string.IsNullOrWhiteSpace(result.OpenAppUrl))
            {
                Console.WriteLine(result.HasWarnings ? "Deploy complete with warnings." : "Deploy complete.");
                Console.WriteLine();
                Console.WriteLine($"Open in Studio: {result.OpenAppUrl}");
                Console.WriteLine("Open now: automaton open");
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
            target = deployResult.TargetSlug,
            targetName = deployResult.TargetName,
            runner = deployResult.RunnerName,
            deploymentMode = deployResult.DeploymentMode,
            openAppUrl = deployResult.OpenAppUrl,
            studioAppUrl = deployResult.StudioAppUrl,
            deployDirectory = deployResult.DeployDirectory,
            clean = deployResult.Clean,
            checks = deployResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
            nextSteps = publishResult.Success
                ? deployResult.NextSteps
                : deployResult.NextSteps.Concat(publishResult.NextSteps).Distinct(StringComparer.OrdinalIgnoreCase),
            sourceUploaded = deployResult.SourceUploaded,
            runtimeUploaded = deployResult.RuntimeUploaded,
            runtime = deployResult.Runtime,
            runtimeRunning = deployResult.RuntimeRunning,
            url = publishResult.Url,
            slug = publishResult.Slug,
            publishChecks = publishResult.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message })
        });
    }

    private static (AutomatonApiClient Client, string Server) CreateApiClient(ConfigService config, string cwd, string? environmentName)
    {
        var appConfig = config.TryLoadProjectConfig(cwd);
        var (server, apiKey) = config.ResolveCredentials(appConfig, cwd, environmentName);
        if (appConfig == null || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Could not resolve app credentials for open.");
        }

        return (new AutomatonApiClient(server!, apiKey!), server!);
    }
}
