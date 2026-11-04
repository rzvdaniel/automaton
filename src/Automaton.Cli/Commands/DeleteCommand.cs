using System.CommandLine;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

/// <summary>Deletes the remote app linked to the current directory while keeping local source files.</summary>
public static class DeleteCommand
{
    public static Command Build(ConfigService config)
    {
        var yes = new Option<bool>("--yes", "Confirm permanent deletion of the linked remote app and its runtime/data");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("delete", "Delete the remote app linked to this folder") { yes, json };
        command.SetHandler(async (bool confirmed, bool asJson) =>
        {
            try
            {
                if (!confirmed) throw new InvalidOperationException("Pass --yes to delete the linked remote app and its runtime/data.");
                var directory = Directory.GetCurrentDirectory();
                var project = config.TryLoadProjectConfig(directory)
                    ?? throw new InvalidOperationException("automaton.json was not found.");
                var context = config.ResolveContext(project, directory);
                if (string.IsNullOrWhiteSpace(context.AppId) || string.IsNullOrWhiteSpace(context.Server) || string.IsNullOrWhiteSpace(context.ApiKey))
                    throw new InvalidOperationException("The current folder needs a linked app and server credentials.");
                using var client = new AutomatonApiClient(context.Server, context.ApiKey);
                await client.DeleteAppAsync(context.AppId);
                if (asJson) CliJson.Write(new { success = true, appId = context.AppId, deleted = true });
                else AnsiConsole.MarkupLine($"[green]Deleted remote app {Markup.Escape(context.AppId)}.[/] Local files remain.");
                Environment.ExitCode = 0;
            }
            catch (Exception exception)
            {
                if (asJson) CliJson.Write(new { success = false, error = exception.Message });
                else AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
                Environment.ExitCode = 1;
            }
        }, yes, json);
        return command;
    }
}
