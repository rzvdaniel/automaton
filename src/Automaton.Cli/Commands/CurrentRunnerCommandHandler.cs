using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

internal static class CurrentRunnerCommandHandler
{
    internal static async Task HandleAsync(ConfigService config, bool asJson)
    {
        try
        {
            var target = await new DeploymentTargetService(config).ResolveAsync(
                Directory.GetCurrentDirectory(), slug: null);
            WriteTarget(target, asJson);
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            WriteFailure(ex, asJson);
            Environment.ExitCode = 1;
        }
    }

    private static void WriteTarget(DeploymentTargetInfo target, bool asJson)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = true,
                target = target.Slug,
                runnerAssigned = target.Runner != null,
                runnerId = target.Runner?.Id,
                runnerName = target.Runner?.Name,
                status = target.Runner?.IsOnline == true ? "connected" : "offline"
            });
            return;
        }

        if (target.Runner == null)
        {
            AnsiConsole.MarkupLine("[yellow]The Live target has no assigned Runner.[/]");
            return;
        }

        AnsiConsole.MarkupLine(
            $"[green]Live environment Runner:[/] {Markup.Escape(target.Runner.Name)} ({Markup.Escape(target.Runner.Id)})");
        AnsiConsole.MarkupLine(target.Runner.IsOnline
            ? "[green]Status:[/] connected"
            : "[yellow]Status:[/] offline");
    }

    private static void WriteFailure(Exception exception, bool asJson)
    {
        if (asJson)
        {
            CliJson.Write(new { success = false, error = exception.Message });
            return;
        }

        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
    }
}
