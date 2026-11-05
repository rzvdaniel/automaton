using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

/// <summary>Upgrades an online Machine Management Runner to the server's current Runner image.</summary>
public static class RunnersUpgradeCommand
{
    public static Command Build(ConfigService config)
    {
        var runner = new Argument<string>("runner", "Personal Runner ID or exact name");
        var wait = new Option<bool>("--wait", "Wait until the Runner reconnected on the new image or was rolled back");
        var timeout = new Option<int>("--timeout", () => 600, "Seconds to wait with --wait");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("upgrade", "Upgrade a Runner to the server's current Runner image; exits non-zero if it fails or rolls back")
            { runner, wait, timeout, json };
        command.SetHandler(async (InvocationContext context) =>
        {
            var reference = context.ParseResult.GetValueForArgument(runner);
            var asJson = context.ParseResult.GetValueForOption(json);
            try
            {
                var (server, apiKey) = config.RequireCredentials(directory: Directory.GetCurrentDirectory());
                using var client = new AutomatonApiClient(server, apiKey);
                var runners = await client.ListRunnersAsync();
                var runnerId = RunnerReferenceResolver.Pick(runners, reference, "id", "name");
                var current = runners.EnumerateArray().First(item => item.GetProperty("id").GetString() == runnerId);
                if (current.TryGetProperty("upgradeAvailable", out var available) && available.ValueKind == JsonValueKind.False)
                {
                    Write(asJson, true, "UpToDate", "The Runner already uses the current image.");
                    return;
                }
                var seconds = context.ParseResult.GetValueForOption(timeout);
                var upgrader = new RunnerUpgradeRunner(client, Task.Delay, text => (asJson ? Console.Error : Console.Out).WriteLine(text));
                var outcome = await upgrader.RunAsync(runnerId, context.ParseResult.GetValueForOption(wait),
                    TimeSpan.FromSeconds(Math.Max(1, seconds)), context.GetCancellationToken());
                Write(asJson, outcome.Success, outcome.State, outcome.Message);
                context.ExitCode = outcome.Success ? 0 : 1;
            }
            catch (Exception exception)
            {
                Write(asJson, false, "Failed", exception.Message);
                context.ExitCode = 1;
            }
        });
        return command;
    }

    private static void Write(bool asJson, bool success, string state, string message)
    {
        if (asJson) CliJson.Write(new { success, state, message });
        else if (success) AnsiConsole.MarkupLine($"[green]{Markup.Escape(message)}[/]");
        else AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
    }
}
