using Automaton.Cli.Models;
using Automaton.Cli.Services;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace Automaton.Cli.Commands;

/// <summary>Lists runners available on the current Automaton account.</summary>
public static class RunnersCommand
{
    public static Command Build(ConfigService config)
    {
        var jsonOption = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command("runners", "List runners available on your Automaton account") { jsonOption };
        var handler = new RunnersCommandHandler(config);
        command.SetHandler(handler.ExecuteAsync, jsonOption);
        return command;
    }
}

internal sealed class RunnersCommandHandler(ConfigService config)
{
    public async Task ExecuteAsync(bool asJson)
    {
        var checks = new List<CommandDiagnosticCheck>();
        var nextSteps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var credentials = ResolveCredentials(checks, nextSteps);
            if (credentials is null)
            {
                WriteFailure(asJson, checks, nextSteps);
                return;
            }

            var resolvedCredentials = credentials.Value;
            var runners = await LoadRunnersAsync(resolvedCredentials);
            var summary = runners.Length == 0 ? "No runners registered" : "Runners loaded";
            var detail = runners.Length == 0 ? "no runners registered" : $"{runners.Length} runner(s) loaded";
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Runners, true, detail));
            var suggestions = runners.Length == 0
                ? new[] { "Create or deploy a runner in Automaton Studio, then rerun automaton runners" }
                : Array.Empty<string>();
            WriteSuccess(asJson, resolvedCredentials.Server, runners, summary, checks, suggestions);
        }
        catch (Exception exception)
        {
            checks.Add(new CommandDiagnosticCheck(CommandDiagnosticCheckNames.Runners, false, exception.Message));
            nextSteps.Add("Run automaton whoami to confirm the current server and identity");
            WriteFailure(asJson, checks, nextSteps);
        }
    }

    private (string Server, string ApiKey)? ResolveCredentials(
        ICollection<CommandDiagnosticCheck> checks,
        ISet<string> nextSteps)
    {
        var (server, apiKey) = config.ResolveCredentials(directory: Directory.GetCurrentDirectory());
        var check = CliCommandChecks.ValidateCredentials(server, apiKey, nextSteps);
        checks.Add(check);
        if (!check.Success || string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
        {
            Environment.ExitCode = 1;
            return null;
        }

        return (server, apiKey);
    }

    private static async Task<RunnerSummary[]> LoadRunnersAsync((string Server, string ApiKey) credentials)
    {
        using var client = new AutomatonApiClient(credentials.Server, credentials.ApiKey);
        var response = await client.ListRunnersAsync();
        if (response.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return response.EnumerateArray().Select(ReadRunner).ToArray();
    }

    private static RunnerSummary ReadRunner(JsonElement runner)
    {
        var id = GetString(runner, "id") ?? "?";
        var name = GetString(runner, "name") ?? "?";
        var connectionId = GetString(runner, "connectionId");
        return new RunnerSummary(id, name, string.IsNullOrEmpty(connectionId) ? "offline" : "connected", connectionId);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString()
            : null;

    private static void WriteSuccess(
        bool asJson,
        string server,
        RunnerSummary[] runners,
        string summary,
        IReadOnlyCollection<CommandDiagnosticCheck> checks,
        IReadOnlyCollection<string> nextSteps)
    {
        if (asJson)
        {
            CliJson.Write(new
            {
                success = true,
                summary,
                checks = checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }),
                nextSteps,
                server,
                runners
            });
            return;
        }

        if (runners.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No runners registered.[/] Start a runner and register it first.");
            return;
        }

        WriteRunnerTable(runners);
    }

    private static void WriteRunnerTable(IEnumerable<RunnerSummary> runners)
    {
        var table = new Table();
        table.AddColumn("Runner");
        table.AddColumn("Status");
        foreach (var runner in runners)
        {
            var statusMarkup = runner.Status == "connected" ? "[green]● connected[/]" : "[grey]○ offline[/]";
            table.AddRow($"{runner.Name} ({runner.Id})", statusMarkup);
        }

        AnsiConsole.Write(table);
    }

    private static void WriteFailure(bool asJson, IReadOnlyCollection<CommandDiagnosticCheck> checks, IEnumerable<string> nextSteps)
    {
        Environment.ExitCode = 1;
        var failure = new CommandDiagnosticResult
        {
            Success = false,
            Summary = "Runner listing failed",
            Checks = checks.ToList(),
            NextSteps = nextSteps.ToList()
        };
        if (asJson)
        {
            CliJson.Write(new { success = false, summary = failure.Summary, checks = failure.Checks.Select(check => new { name = check.Name, success = check.Success, warning = check.IsWarning, error = check.Message }), nextSteps = failure.NextSteps });
            return;
        }

        CliDiagnosticsWriter.Write(failure);
    }

    private sealed record RunnerSummary(string Id, string Name, string Status, string? ConnectionId);
}
