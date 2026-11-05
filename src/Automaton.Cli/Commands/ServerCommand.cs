using System.CommandLine;
using System.CommandLine.Invocation;
using Automaton.Cli.Models;
using Automaton.Cli.Services;

namespace Automaton.Cli.Commands;

/// <summary>Updates the connected Automaton server and reports the result; for system administrators.</summary>
internal static class ServerCommand
{
    internal static Command Build(ConfigService config)
    {
        var command = new Command("server", "Update the Automaton server you are logged in to");
        command.AddCommand(BuildUpdate(config));
        command.AddCommand(BuildUpdateStatus(config));
        return command;
    }

    private static Command BuildUpdate(ConfigService config)
    {
        var version = new Option<string?>("--version", "Release to install: latest (default), server-vX.Y.Z or X.Y.Z");
        var wait = new Option<bool>("--wait", "Wait for the update to finish, then verify /health and the installed version");
        var timeout = new Option<int>("--timeout", () => 1800, "Seconds to wait with --wait");
        var downgrade = new Option<bool>("--allow-downgrade", "Allow installing a release older than the installed one");
        var runners = new Option<bool>("--runners", "After a waited update, also upgrade the Runners that are on an old image");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("update", "Update the server to the latest release or a given version") { version, wait, timeout, downgrade, runners, json };
        command.SetHandler(context => RunAsync(context, config, async (client, asJson, ct) =>
        {
            var requested = ServerUpdateRunner.NormalizeVersion(context.ParseResult.GetValueForOption(version))
                ?? throw new InvalidOperationException("Use --version latest, server-vX.Y.Z or X.Y.Z.");
            var seconds = context.ParseResult.GetValueForOption(timeout);
            if (seconds < 1) throw new InvalidOperationException("--timeout must be 1 or more seconds.");
            var runner = new ServerUpdateRunner(client, Task.Delay, text => (asJson ? Console.Error : Console.Out).WriteLine(text));
            var outcome = await runner.RunAsync(requested, context.ParseResult.GetValueForOption(wait), TimeSpan.FromSeconds(seconds), ct,
                context.ParseResult.GetValueForOption(downgrade));
            WriteOutcome(outcome, asJson);
            if (!outcome.Success || !outcome.Waited) return outcome.Success;
            return await ReportRunnersAsync(client, context.ParseResult.GetValueForOption(runners), asJson, ct);
        }, json));
        return command;
    }

    /// <summary>Lists Runners left on an old image with the command to upgrade them; with upgrade, upgrades them too.</summary>
    private static async Task<bool> ReportRunnersAsync(AutomatonApiClient client, bool upgrade, bool asJson, CancellationToken ct)
    {
        var old = (await client.ListRunnersAsync(ct)).EnumerateArray()
            .Where(runner => runner.TryGetProperty("upgradeAvailable", out var flag) && flag.ValueKind == System.Text.Json.JsonValueKind.True)
            .Select(runner => (Id: runner.GetProperty("id").GetString() ?? "", Name: runner.GetProperty("name").GetString() ?? "?"))
            .ToList();
        var results = new List<object>();
        var success = true;
        foreach (var (id, name) in old)
        {
            var command = $"automaton runners upgrade {id} --wait";
            if (!upgrade)
            {
                (asJson ? Console.Error : Console.Out).WriteLine($"Runner {name} is on an old image. Upgrade it with: {command}");
                results.Add(new { id, name, upgraded = false, command });
                continue;
            }
            var upgrader = new RunnerUpgradeRunner(client, Task.Delay, text => (asJson ? Console.Error : Console.Out).WriteLine($"{name}: {text}"));
            RunnerUpgradeOutcome outcome;
            try { outcome = await upgrader.RunAsync(id, true, TimeSpan.FromMinutes(10), ct); }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                outcome = new(false, "Failed", exception.Message + $" Run on its server instead, or retry: {command}");
            }
            success &= outcome.Success;
            (asJson ? Console.Error : Console.Out).WriteLine($"Runner {name}: {outcome.Message}");
            results.Add(new { id, name, upgraded = outcome.Success, state = outcome.State, outcome.Message });
        }
        if (asJson && results.Count > 0) CliJson.Write(new { runners = results });
        return success;
    }

    private static Command BuildUpdateStatus(ConfigService config)
    {
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("update-status", "Show the last server update and its log") { json };
        command.SetHandler(context => RunAsync(context, config, async (client, asJson, ct) =>
        {
            var status = await client.GetServerUpdateAsync(ct);
            if (asJson) CliJson.Write(new { success = true, status.CurrentVersion, status.TriggerAvailable, status.Run, status.Log });
            else WriteStatus(status);
            return true;
        }, json));
        return command;
    }

    private static async Task RunAsync(
        InvocationContext context, ConfigService config, Func<AutomatonApiClient, bool, CancellationToken, Task<bool>> body, Option<bool> json)
    {
        var asJson = context.ParseResult.GetValueForOption(json);
        try
        {
            var (server, apiKey) = config.ResolveCredentials(directory: Directory.GetCurrentDirectory());
            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Run automaton login to connect to your server.");
            using var client = new AutomatonApiClient(server, apiKey);
            if (!await body(client, asJson, context.GetCancellationToken())) context.ExitCode = 1;
        }
        catch (Exception exception)
        {
            if (asJson) CliJson.Write(new { success = false, error = exception.Message });
            else Console.Error.WriteLine(exception.Message);
            context.ExitCode = 1;
        }
    }

    private static void WriteOutcome(ServerUpdateOutcome outcome, bool asJson)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = outcome.Success,
                message = outcome.Message,
                requestedVersion = outcome.RequestedVersion,
                run = outcome.Run,
                log = outcome.Log
            });
            return;
        }
        if (outcome.Success)
        {
            Console.WriteLine(outcome.Message);
            return;
        }
        Console.Error.WriteLine(outcome.Message);
        if (!string.IsNullOrWhiteSpace(outcome.Log))
        {
            Console.Error.WriteLine("Last log lines:");
            foreach (var line in outcome.Log.TrimEnd().Split('\n').TakeLast(40)) Console.Error.WriteLine("  " + line.TrimEnd('\r'));
        }
    }

    private static void WriteStatus(ServerUpdateStatus status)
    {
        Console.WriteLine($"Current version: {status.CurrentVersion ?? "Unknown"}");
        if (!status.TriggerAvailable)
            Console.WriteLine("Updates from Studio and the CLI are not enabled on this server. Run sudo bash /opt/automaton-server/update.sh once on the server.");
        if (status.Run is not { } run)
        {
            Console.WriteLine("No update has been requested yet.");
            return;
        }
        Console.WriteLine($"Last update: {run.State}");
        Console.WriteLine($"Requested version: {run.RequestedVersion ?? "-"}");
        Console.WriteLine($"From: {run.FromVersion ?? "-"}  To: {run.ToVersion ?? "-"}");
        Console.WriteLine($"Started: {run.StartedAtUtc?.ToString("O") ?? "-"}  Finished: {run.FinishedAtUtc?.ToString("O") ?? "-"}");
        if (run.RecoveryBackup is not null) Console.WriteLine($"Recovery backup: {run.RecoveryBackup}");
        if (run.Message is not null) Console.WriteLine(run.Message);
        if (!string.IsNullOrWhiteSpace(status.Log))
        {
            Console.WriteLine("Log:");
            Console.WriteLine(status.Log.TrimEnd());
        }
    }
}
