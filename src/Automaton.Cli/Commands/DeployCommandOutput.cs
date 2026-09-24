using System.Diagnostics;
using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

internal static class DeployCommandOutput
{
    private static readonly Stopwatch managedRunnerWaitTimer = new();
    private static int managedRunnerSpinnerFrame;
    private static DateTime managedRunnerLastNonInteractiveUpdateUtc;
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
        if (nonInteractive) WritePlainProgress(message);
        else WriteInteractiveProgress(message);
    }

    private static void WritePlainProgress(string message)
    {
        if (IsManagedRuntimePreparation(message))
        {
            managedRunnerWaitTimer.Restart();
            managedRunnerLastNonInteractiveUpdateUtc = DateTime.UtcNow;
            Console.WriteLine(message);
            return;
        }

        if (IsWaitingForManagedRunner(message))
        {
            WritePlainWaitProgress();
            return;
        }

        if (IsManagedRunnerConnected(message))
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
        if (IsManagedRuntimePreparation(message))
        {
            managedRunnerWaitTimer.Restart();
            managedRunnerSpinnerFrame = 0;
            AnsiConsole.MarkupLine("[grey]Managed runtime is being prepared...[/]");
            return;
        }

        if (IsWaitingForManagedRunner(message))
        {
            WriteSpinnerFrame();
            return;
        }

        if (IsManagedRunnerConnected(message))
        {
            Console.Write("\r\u001b[2K");
            AnsiConsole.MarkupLine($"[green]✓ Managed runner connected after {managedRunnerWaitTimer.Elapsed.TotalSeconds:F0}s.[/]");
            managedRunnerWaitTimer.Stop();
            return;
        }

        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]");
    }

    private static bool IsManagedRuntimePreparation(string message) =>
        message.Contains("Managed runtime is being prepared", StringComparison.OrdinalIgnoreCase);

    private static bool IsWaitingForManagedRunner(string message) =>
        message.Contains("Waiting for managed runner", StringComparison.OrdinalIgnoreCase);

    private static bool IsManagedRunnerConnected(string message) =>
        message.Contains("Managed runner connected", StringComparison.OrdinalIgnoreCase);

    private static void WriteSpinnerFrame()
    {
        var frames = Console.OutputEncoding.CodePage == 65001
            ? new[] { '⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏' }
            : new[] { '|', '/', '-', '\\' };
        var frame = frames[managedRunnerSpinnerFrame++ % frames.Length];
        Console.Write($"\r{frame}");
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
        if (asJson || headingWritten || string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(appId))
        {
            return;
        }

        if (nonInteractive)
        {
            Console.WriteLine($"Deploying {appName} ({appId})");
            Console.WriteLine($"Local profile: {profileName ?? "default credentials"}");
            Console.WriteLine($"Deployment target: {target.Name} ({target.Slug})");
            if (!string.IsNullOrWhiteSpace(server))
            {
                Console.WriteLine($"Server: {server}");
            }
        }
        else
        {
            AnsiConsole.MarkupLine($"[bold]Deploying[/] [cyan]{appName}[/] ({appId})");
            AnsiConsole.MarkupLine($"[bold]Local profile:[/] {Markup.Escape(profileName ?? "default credentials")}");
            AnsiConsole.MarkupLine($"[bold]Deployment target:[/] {Markup.Escape(target.Name)} ({Markup.Escape(target.Slug)})");
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
