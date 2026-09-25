using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.Text.Json;

namespace Automaton.Cli.Commands;

internal static class RunnersCommandHandler
{
    internal static async Task HandleAsync(ConfigService config, bool asJson)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var credentials = ResolveCredentials(config, checks, nextSteps);
            if (credentials == null)
            {
                WriteFailure(checks, nextSteps, asJson);
                Environment.ExitCode = 1;
                return;
            }

            var runners = await LoadRunnersAsync(credentials);
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.Runners,
                true,
                runners.Count == 0 ? "no runners registered" : $"{runners.Count} runner(s) loaded"));
            WriteSuccess(credentials.Server, runners, checks, asJson);
        }
        catch (Exception exception)
        {
            checks.Add(new CommandDiagnosticCheck(
                CommandDiagnosticCheckNames.Runners, false, exception.Message));
            nextSteps.Add("Run automaton whoami to confirm the current server and identity");
            WriteFailure(checks, nextSteps, asJson);
            Environment.ExitCode = 1;
        }
    }

    private static RunnerCredentials? ResolveCredentials(
        ConfigService config,
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var (server, apiKey) = config.ResolveCredentials(directory: Directory.GetCurrentDirectory());
        var check = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
        checks.Add(check);
        return check.Success && !string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(apiKey)
            ? new RunnerCredentials(server, apiKey)
            : null;
    }

    private static async Task<IReadOnlyList<RunnerListItem>> LoadRunnersAsync(RunnerCredentials credentials)
    {
        using var client = new AutomatonApiClient(credentials.Server, credentials.ApiKey);
        var response = await client.ListRunnersAsync();
        if (response.ValueKind != JsonValueKind.Array) return [];
        return response.EnumerateArray().Select(ReadRunner).ToArray();
    }

    private static RunnerListItem ReadRunner(JsonElement runner)
    {
        var id = ReadString(runner, "id") ?? "?";
        var name = ReadString(runner, "name") ?? "?";
        var connectionId = ReadConnectionId(runner);
        return new RunnerListItem(id, name, connectionId);
    }

    private static string? ReadString(JsonElement value, string property)
        => value.TryGetProperty(property, out var result) ? result.GetString() : null;

    private static string? ReadConnectionId(JsonElement runner)
    {
        if (!runner.TryGetProperty("connectionId", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return string.IsNullOrEmpty(value.GetString()) ? null : value.GetString();
    }

    private static void WriteSuccess(
        string server,
        IReadOnlyList<RunnerListItem> runners,
        IReadOnlyCollection<CommandDiagnosticCheck> checks,
        bool asJson)
    {
        if (asJson)
        {
            WriteJson(server, runners, checks);
            return;
        }
        if (runners.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No runners registered.[/] Start a runner and register it first.");
            return;
        }
        AnsiConsole.Write(BuildTable(runners));
    }

    private static void WriteJson(
        string server,
        IReadOnlyList<RunnerListItem> runners,
        IEnumerable<CommandDiagnosticCheck> checks)
    {
        var summary = runners.Count == 0 ? "No runners registered" : "Runners loaded";
        var nextSteps = runners.Count == 0
            ? new[] { "Create or deploy a runner in Automaton Studio, then rerun automaton runners" }
            : Array.Empty<string>();
        CliJson.Write(new
        {
            success = true,
            summary,
            checks = MapChecks(checks),
            nextSteps,
            server,
            runners = runners.Select(runner => new
            {
                id = runner.Id,
                name = runner.Name,
                status = runner.Connected ? "connected" : "offline",
                connectionId = runner.ConnectionId
            })
        });
    }

    private static Table BuildTable(IEnumerable<RunnerListItem> runners)
    {
        var table = new Table();
        table.AddColumn("Runner");
        table.AddColumn("Status");
        foreach (var runner in runners)
        {
            var label = $"{runner.Name} ({runner.Id})";
            var status = runner.Connected ? "[green]● connected[/]" : "[grey]○ offline[/]";
            table.AddRow(label, status);
        }
        return table;
    }

    private static IEnumerable<object> MapChecks(IEnumerable<CommandDiagnosticCheck> checks)
        => checks.Select(check => new
        {
            name = check.Name,
            success = check.Success,
            warning = check.IsWarning,
            error = check.Message
        });

    private static void WriteFailure(
        IEnumerable<CommandDiagnosticCheck> checks,
        IEnumerable<string> nextSteps,
        bool asJson)
    {
        var result = new CommandDiagnosticResult
        {
            Success = false,
            Summary = "Runner listing failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList()
        };
        if (asJson)
        {
            CliJson.Write(new
            {
                success = false,
                summary = result.Summary,
                checks = MapChecks(result.Checks),
                nextSteps = result.NextSteps
            });
            return;
        }
        CliDiagnosticsWriter.Write(result);
    }

    private sealed record RunnerCredentials(string Server, string ApiKey);
    private sealed record RunnerListItem(string Id, string Name, string? ConnectionId)
    {
        internal bool Connected => ConnectionId != null;
    }
}
