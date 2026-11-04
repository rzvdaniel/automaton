using System.CommandLine;
using System.CommandLine.Invocation;
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
        command.SetHandler(async (InvocationContext context) =>
        {
            var confirmed = context.ParseResult.GetValueForOption(yes);
            var asJson = context.ParseResult.GetValueForOption(json);
            try
            {
                if (!confirmed) throw new InvalidOperationException("Pass --yes to delete the linked remote app and its runtime/data.");
                var directory = Directory.GetCurrentDirectory();
                var project = config.TryLoadProjectConfig(directory)
                    ?? throw new InvalidOperationException("automaton.json was not found.");
                var appContext = config.ResolveContext(project, directory);
                if (string.IsNullOrWhiteSpace(appContext.AppId) || string.IsNullOrWhiteSpace(appContext.Server) || string.IsNullOrWhiteSpace(appContext.ApiKey))
                    throw new InvalidOperationException("The current folder needs a linked app and server credentials.");
                using var client = new AutomatonApiClient(appContext.Server, appContext.ApiKey);
                await client.DeleteAppAsync(appContext.AppId);
                if (asJson) CliJson.Write(new { success = true, appId = appContext.AppId, deleted = true });
                else AnsiConsole.MarkupLine($"[green]Deleted remote app {Markup.Escape(appContext.AppId)}.[/] Local files remain.");
                context.ExitCode = 0;
            }
            catch (Exception exception)
            {
                if (asJson) CliJson.Write(new { success = false, error = exception.Message });
                else AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
                context.ExitCode = 1;
            }
        });
        return command;
    }
}
