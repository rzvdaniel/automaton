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
        var abandon = new Option<bool>("--abandon-offline",
            "Administrator: the Runner is offline for good; delete it and the apps assigned to it without cleaning up its machine (containers may remain)");
        var yes = new Option<bool>("--yes", "Confirm permanent deletion of the apps with --abandon-offline");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("delete", "Prepare Runner removal, then confirm host cleanup to delete its registration") { runner, confirmed, ssh, abandon, yes, json };
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
                if (context.ParseResult.GetValueForOption(abandon))
                {
                    await DeleteOfflineAsync(client, runnerId, context.ParseResult.GetValueForOption(yes), asJson, context.GetCancellationToken());
                    context.ExitCode = 0;
                    return;
                }
                var removal = await client.PrepareRunnerRemovalAsync(runnerId);
                if (sshTarget is not null)
                {
                    var script = removal.GetProperty("uninstallCommand").GetString() ?? throw new InvalidOperationException("Uninstall command missing.");
                    await RunnersInstallCommand.ExecuteAsync(script, sshTarget, 300, context.GetCancellationToken(), "Uninstall");
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
                        WriteUnwrapped(uninstallCommand);
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

    private static async Task DeleteOfflineAsync(AutomatonApiClient client, string runnerId, bool confirmed, bool asJson, CancellationToken ct)
    {
        if (!confirmed)
            throw new InvalidOperationException("Pass --yes to delete the offline Runner and every app assigned to it without cleaning up its machine. This cannot be undone.");
        var skipped = await client.DeleteOfflineRunnerAsync(runnerId, ct);
        if (asJson) CliJson.Write(new { success = true, runnerId, deleted = true, skippedHostCleanup = skipped });
        else
        {
            AnsiConsole.MarkupLine($"[green]Deleted Runner registration {Markup.Escape(runnerId)} and its apps.[/]");
            foreach (var line in skipped) AnsiConsole.MarkupLine($"[yellow]Skipped cleanup:[/] {Markup.Escape(line)}");
        }
    }

    /// <summary>Prints the command as one line; Spectre would wrap it at the terminal width and break copy and paste.</summary>
    public static void WriteUnwrapped(string text) => Console.Out.WriteLine(text);
}
