using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

internal static class SetRunnerCommandHandler
{
    internal static async Task HandleAsync(ConfigService config, string? runner, bool clear, bool asJson, bool cleanupRuntime = false)
    {
        try
        {
            EnsureSelection(runner, clear);
            var target = await UpdateAssignmentAsync(config, runner, clear, cleanupRuntime);
            WriteSuccess(target, runner, clear, asJson);
            Environment.ExitCode = 0;
        }
        catch (Exception exception)
        {
            WriteFailure(exception, runner, clear, asJson);
            Environment.ExitCode = 1;
        }
    }

    private static void EnsureSelection(string? runner, bool clear)
    {
        if (!clear && string.IsNullOrWhiteSpace(runner))
        {
            throw new InvalidOperationException(
                "Provide a Runner ID, slug, or exact name, or use --clear.");
        }
    }

    private static async Task<DeploymentTargetInfo> UpdateAssignmentAsync(
        ConfigService config, string? runner, bool clear, bool cleanupRuntime)
    {
        var directory = Directory.GetCurrentDirectory();
        var service = new DeploymentTargetService(config);
        var target = await service.ResolveAsync(directory, slug: null);
        return clear
            ? await service.ClearRunnerAsync(directory, target, cleanupRuntime: cleanupRuntime)
            : await service.AssignRunnerAsync(directory, target, runner!, cleanupRuntime: cleanupRuntime);
    }

    private static void WriteSuccess(
        DeploymentTargetInfo target, string? runner, bool clear, bool asJson)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = true,
                target = target.Slug,
                action = clear ? "clear" : "assign",
                runnerId = target.Runner?.Id,
                runnerName = target.Runner?.Name
            });
            return;
        }

        AnsiConsole.MarkupLine(clear
            ? "[green]Cleared the Live target's Runner assignment.[/]"
            : $"[green]Live target Runner:[/] {Markup.Escape(target.Runner?.Name ?? runner!)}");
    }

    private static void WriteFailure(Exception exception, string? runner, bool clear, bool asJson)
    {
        var missingRunner = !clear && string.IsNullOrWhiteSpace(runner);
        var check = new CommandDiagnosticCheck(
            missingRunner ? CommandDiagnosticCheckNames.RunnerLookup : CommandDiagnosticCheckNames.RunnerAssignment,
            false,
            exception.Message);
        if (asJson)
        {
            CliJson.Write(new
            {
                success = false,
                summary = "Runner assignment failed",
                checks = new[] { check },
                nextSteps = new[]
                {
                    "Run automaton target list and automaton runners to inspect available assignments"
                }
            });
            return;
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
    }
}
