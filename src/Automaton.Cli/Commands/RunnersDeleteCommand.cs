using System.CommandLine;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

/// <summary>Runs the same two-step Runner removal flow as Studio.</summary>
public static class RunnersDeleteCommand
{
    public static Command Build(ConfigService config)
    {
        var runner = new Argument<string>("runner", "Personal Runner ID or exact name");
        var confirmed = new Option<bool>("--host-cleanup-confirmed", "Confirm the uninstall command completed on the Runner host");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("delete", "Prepare Runner removal, then confirm host cleanup to delete its registration") { runner, confirmed, json };
        command.SetHandler(async (string reference, bool hostCleanupConfirmed, bool asJson) =>
        {
            try
            {
                var (server, apiKey) = config.RequireCredentials(directory: Directory.GetCurrentDirectory());
                using var client = new AutomatonApiClient(server, apiKey);
                var runnerId = RunnerReferenceResolver.Pick(await client.ListRunnersAsync(), reference, "id", "name");
                var removal = await client.PrepareRunnerRemovalAsync(runnerId);
                if (hostCleanupConfirmed)
                {
                    await client.DeleteRunnerAsync(runnerId);
                    if (asJson) CliJson.Write(new { success = true, runnerId, deleted = true });
                    else AnsiConsole.MarkupLine($"[green]Deleted Runner registration {Markup.Escape(runnerId)}.[/]");
                }
                else
                {
                    var uninstallCommand = removal.GetProperty("uninstallCommand").GetString() ?? "";
                    if (asJson) CliJson.Write(new { success = true, runnerId, deleted = false, uninstallCommand });
                    else
                    {
                        AnsiConsole.MarkupLine("[yellow]Runner removal prepared.[/] Run this command on its host:");
                        AnsiConsole.WriteLine(uninstallCommand);
                        AnsiConsole.MarkupLine($"Then run [bold]automaton runners delete {Markup.Escape(runnerId)} --host-cleanup-confirmed[/].");
                    }
                }
                Environment.ExitCode = 0;
            }
            catch (Exception exception)
            {
                if (asJson) CliJson.Write(new { success = false, error = exception.Message });
                else AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
                Environment.ExitCode = 1;
            }
        }, runner, confirmed, json);
        return command;
    }
}
