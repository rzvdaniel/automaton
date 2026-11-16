using System.CommandLine;
using System.CommandLine.Invocation;
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
        var ssh = new Option<string?>("--ssh", "Run the uninstall command on the host over SSH as user@host, then delete the registration");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("delete", "Prepare Runner removal, then confirm host cleanup to delete its registration") { runner, confirmed, ssh, json };
        command.SetHandler(async (InvocationContext context) =>
        {
            var reference = context.ParseResult.GetValueForArgument(runner);
            var hostCleanupConfirmed = context.ParseResult.GetValueForOption(confirmed);
            var sshTarget = context.ParseResult.GetValueForOption(ssh);
            var asJson = context.ParseResult.GetValueForOption(json);
            try
            {
                var (server, apiKey) = config.RequireCredentials(directory: Directory.GetCurrentDirectory());
                using var client = new AutomatonApiClient(server, apiKey);
                var runnerId = RunnerReferenceResolver.Pick(await client.ListRunnersAsync(), reference, "id", "name");
                if (sshTarget is not null && (sshTarget.Length == 0 || sshTarget.StartsWith('-') || sshTarget.Any(char.IsWhiteSpace)))
                    throw new ArgumentException("--ssh requires a user@host without whitespace or options.");
                var removal = await client.PrepareRunnerRemovalAsync(runnerId);
                if (sshTarget is not null)
                {
                    var script = removal.GetProperty("uninstallCommand").GetString() ?? throw new InvalidOperationException("Uninstall command missing.");
                    await RunnersInstallCommand.ExecuteAsync(script, sshTarget, 300, context.GetCancellationToken());
                    hostCleanupConfirmed = true;
                }
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
