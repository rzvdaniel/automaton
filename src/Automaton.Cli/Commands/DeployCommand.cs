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
            "Remote deployment target slug; omit to deploy the only target or choose interactively")
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
        var environmentOption = new Option<string?>("--env", "Use a local project linkage/credential profile from ./.automaton/environments/<name>.json");

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

        cmd.SetHandler(context => DeployCommandHandler.HandleAsync(
            config,
            context,
            targetArgument,
            runnerOption,
            cleanOption,
            dirOption,
            jsonOption,
            verboseOption,
            publishOption,
            openOption,
            nonInteractiveOption,
            environmentOption));
        return cmd;
    }

    internal static DeployResult MergePreparation(DeployResult result, DeployPreparationResult preparation)
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

    internal static DeployResult ApplyTargetIdentity(
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

    internal static DeployResult AppendDefaultSuccessNextSteps(DeployResult result)
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

    internal static void WriteResult(DeployResult result, bool asJson, bool verbose, bool nonInteractive)
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
                $"[bold]Deployment target:[/] {Markup.Escape(result.TargetName ?? result.TargetSlug ?? "Live")} " +
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

    internal static void WriteProgress(string message, bool asJson, bool nonInteractive)
    {
        if (asJson || string.IsNullOrWhiteSpace(message)) return;
        if (nonInteractive)
        {
            WritePlainProgress(message);
            return;
        }
        WriteInteractiveProgress(message);
    }

    private static void WritePlainProgress(string message)
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
            WritePlainWaitProgress();
            return;
        }
        if (message.Contains("Managed runner connected", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Managed runner connected after {managedRunnerWaitTimer.Elapsed.TotalSeconds:F0}s.");
            managedRunnerWaitTimer.Stop();
            return;
        }
        Console.WriteLine(message);
    }

    private static void WritePlainWaitProgress()
    {
        if (DateTime.UtcNow - managedRunnerLastNonInteractiveUpdateUtc < TimeSpan.FromSeconds(15)) return;
        managedRunnerLastNonInteractiveUpdateUtc = DateTime.UtcNow;
        Console.WriteLine($"Still waiting for managed runner... {managedRunnerWaitTimer.Elapsed.TotalSeconds:F0}s");
    }

    private static void WriteInteractiveProgress(string message)
    {
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

    internal static void WriteCheck(DeployCheckResult check, bool asJson, bool nonInteractive)
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

    internal static void WriteDeployHeading(
        string? appName,
        string? appId,
        string? profileName,
        DeploymentTargetInfo target,
        string? server,
        bool asJson,
        bool nonInteractive,
        ref bool headingWritten)
    {
        if (!CanWriteHeading(appName, appId, asJson, headingWritten)) return;
        if (nonInteractive)
        {
            WritePlainHeading(appName!, appId!, profileName, target, server);
        }
        else
        {
            WriteRichHeading(appName!, appId!, profileName, target, server);
        }
        headingWritten = true;
    }

    private static bool CanWriteHeading(string? appName, string? appId, bool asJson, bool alreadyWritten)
        => !asJson && !alreadyWritten &&
           !string.IsNullOrWhiteSpace(appName) && !string.IsNullOrWhiteSpace(appId);

    private static void WritePlainHeading(
        string appName, string appId, string? profileName, DeploymentTargetInfo target, string? server)
    {
        Console.WriteLine($"Deploying {appName} ({appId})");
        Console.WriteLine($"Local profile: {profileName ?? "default credentials"}");
        Console.WriteLine($"Deployment target: {target.Name} ({target.Slug})");
        if (!string.IsNullOrWhiteSpace(server)) Console.WriteLine($"Server: {server}");
    }

    private static void WriteRichHeading(
        string appName, string appId, string? profileName, DeploymentTargetInfo target, string? server)
    {
        AnsiConsole.MarkupLine($"[bold]Deploying[/] [cyan]{appName}[/] ({appId})");
        AnsiConsole.MarkupLine($"[bold]Local profile:[/] {Markup.Escape(profileName ?? "default credentials")}");
        AnsiConsole.MarkupLine($"[bold]Deployment target:[/] {Markup.Escape(target.Name)} ({Markup.Escape(target.Slug)})");
        if (!string.IsNullOrWhiteSpace(server))
        {
            AnsiConsole.MarkupLine($"[bold]Server:[/] {Markup.Escape(server)}");
        }
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
                $"Deployment target: {result.TargetName ?? result.TargetSlug ?? "Live"} ({result.DeploymentMode ?? "Live"})");
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

    internal static void WriteCombinedJson(DeployResult deployResult, PublishResult publishResult)
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

    internal static (AutomatonApiClient Client, string Server) CreateApiClient(ConfigService config, string cwd, string? environmentName)
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
