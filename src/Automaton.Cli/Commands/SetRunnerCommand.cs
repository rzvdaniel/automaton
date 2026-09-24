using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class SetRunnerCommand
{
    public static Command Build(ConfigService config)
    {
        var runnerArgument = new Argument<string?>(
            "runner", () => null, "Runner ID, slug, or exact Runner name");
        var clearOption = new Option<bool>(
            "--clear", "Clear the Live target's Runner assignment");
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command(
            "set-runner",
            "Assign or clear the Runner for the Live environment")
        {
            runnerArgument,
            clearOption,
            jsonOption
        };
        command.SetHandler(
            (string? runner, bool clear, bool asJson) => HandleSetRunnerAsync(config, runner, clear, asJson),
            runnerArgument,
            clearOption,
            jsonOption);
        return command;
    }

    private static async Task HandleSetRunnerAsync(
        ConfigService config,
        string? runner,
        bool clear,
        bool asJson)
    {
        try
        {
            ValidateRunnerSelection(runner, clear);
            var target = await AssignRunnerAsync(config, runner, clear);
            WriteSuccess(target, runner, clear, asJson);
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            WriteFailure(ex, runner, clear, asJson);
            Environment.ExitCode = 1;
        }
    }

    private static void ValidateRunnerSelection(string? runner, bool clear)
    {
        if (!clear && string.IsNullOrWhiteSpace(runner))
            throw new InvalidOperationException("Provide a Runner ID, slug, or exact name, or use --clear.");
    }

    private static async Task<DeploymentTargetInfo> AssignRunnerAsync(
        ConfigService config,
        string? runner,
        bool clear)
    {
        var directory = Directory.GetCurrentDirectory();
        var service = new DeploymentTargetService(config);
        var target = await service.ResolveAsync(directory, slug: null);
        return clear
            ? await service.ClearRunnerAsync(directory, target)
            : await service.AssignRunnerAsync(directory, target, runner!);
    }

    private static void WriteSuccess(
        DeploymentTargetInfo target,
        string? runner,
        bool clear,
        bool asJson)
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
        var checkName = !clear && string.IsNullOrWhiteSpace(runner)
            ? CommandDiagnosticCheckNames.RunnerLookup
            : CommandDiagnosticCheckNames.RunnerAssignment;
        var checks = new[]
        {
            new CommandDiagnosticCheck(checkName, false, exception.Message)
        };
        var nextSteps = new[]
        {
            "Run automaton target list and automaton runners to inspect available assignments"
        };

        if (asJson)
        {
            CliJson.Write(new
            {
                success = false,
                summary = "Runner assignment failed",
                checks,
                nextSteps
            });
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
        }
    }
}
