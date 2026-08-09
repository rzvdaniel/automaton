using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;

namespace Automaton.Cli.Commands;

public static class CurrentRunnerCommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command(
            "current-runner",
            "Show the Runner assigned to the Live environment")
        {
            jsonOption
        };
        command.SetHandler(async (bool asJson) =>
        {
            try
            {
                var target = await new DeploymentTargetService(config).ResolveAsync(
                    Directory.GetCurrentDirectory(), slug: null);
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
                }
                else if (target.Runner == null)
                {
                    AnsiConsole.MarkupLine(
                        "[yellow]The Live target has no assigned Runner.[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine(
                        $"[green]Live environment Runner:[/] {Markup.Escape(target.Runner.Name)} ({Markup.Escape(target.Runner.Id)})");
                    AnsiConsole.MarkupLine(target.Runner.IsOnline
                        ? "[green]Status:[/] connected"
                        : "[yellow]Status:[/] offline");
                }
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                if (asJson)
                {
                    CliJson.Write(new { success = false, error = ex.Message });
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
                }
                Environment.ExitCode = 1;
            }
        }, jsonOption);
        return command;
    }
}
