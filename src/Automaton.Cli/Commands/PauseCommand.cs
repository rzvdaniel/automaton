using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Automaton.Cli.Services;
using Spectre.Console;

namespace Automaton.Cli.Commands;

/// <summary>
/// Pauses an app without deleting it (stops it, switches its schedules off and makes its address answer "paused") and
/// resumes it later. <c>automaton pause</c> and <c>automaton resume</c> act on the linked app folder;
/// <c>automaton apps pause|resume APP</c> acts on any app you can deploy.
/// </summary>
public static class PauseCommand
{
    public static Command BuildPause(ConfigService config) => Build(config, paused: true);

    public static Command BuildResume(ConfigService config) => Build(config, paused: false);

    private static Command Build(ConfigService config, bool paused)
    {
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command(paused ? "pause" : "resume", paused
            ? "Pause the linked app: stop it, switch its schedules off and show a paused page at its address. Data and settings are kept."
            : "Resume the linked app and turn its schedules back on")
        { json };
        command.SetHandler(async (InvocationContext context) =>
        {
            var asJson = context.ParseResult.GetValueForOption(json);
            try
            {
                var appId = ResolveLinkedAppId(config, out var server, out var apiKey);
                using var client = new AutomatonApiClient(server, apiKey);
                Write(await client.SetAppPausedAsync(appId, paused, context.GetCancellationToken()), appId, null, asJson);
            }
            catch (Exception exception)
            {
                WriteFailure(exception, asJson);
                context.ExitCode = 1;
            }
        });
        return command;
    }

    internal static Command BuildForApps(ConfigService config, bool paused)
    {
        var app = new Argument<string>("app", "App ID, exact slug or exact name");
        var json = new Option<bool>("--json", "Print the result as JSON");
        var command = new Command(paused ? "pause" : "resume", paused
            ? "Pause an app: stop it, switch its schedules off and show a paused page at its address. Data and settings are kept."
            : "Resume a paused app and turn its schedules back on")
        { app, json };
        command.SetHandler(async (InvocationContext context) =>
        {
            var asJson = context.ParseResult.GetValueForOption(json);
            try
            {
                var (server, apiKey) = config.ResolveCredentials(directory: Directory.GetCurrentDirectory());
                if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
                    throw new InvalidOperationException("Run automaton login to connect to your server.");
                using var client = new AutomatonApiClient(server, apiKey);
                var inventory = await client.ListAppInventoryAsync(context.GetCancellationToken());
                var id = InventoryCommands.ResolveAppId(inventory, context.ParseResult.GetValueForArgument(app));
                var name = inventory.FirstOrDefault(item => item.Id.ToString() == id.ToLowerInvariant())?.Name;
                Write(await client.SetAppPausedAsync(id, paused, context.GetCancellationToken()), id, name, asJson);
            }
            catch (Exception exception)
            {
                WriteFailure(exception, asJson);
                context.ExitCode = 1;
            }
        });
        return command;
    }

    /// <summary>Resumes the linked app before a deploy, for <c>automaton deploy --resume</c>.</summary>
    internal static async Task ResumeLinkedAsync(ConfigService config, string? environmentName, CancellationToken cancellationToken)
    {
        var appId = ResolveLinkedAppId(config, out var server, out var apiKey, environmentName);
        using var client = new AutomatonApiClient(server, apiKey);
        await client.SetAppPausedAsync(appId, paused: false, cancellationToken);
    }

    private static string ResolveLinkedAppId(ConfigService config, out string server, out string apiKey, string? environmentName = null)
    {
        var directory = Directory.GetCurrentDirectory();
        var project = config.TryLoadProjectConfig(directory)
            ?? throw new InvalidOperationException("automaton.json was not found. Run this in an app folder, or use: automaton apps pause APP");
        var resolved = config.ResolveContext(project, directory, environmentName);
        if (string.IsNullOrWhiteSpace(resolved.AppId))
            throw new InvalidOperationException("The project is not linked to an Automaton app.");
        if (string.IsNullOrWhiteSpace(resolved.Server) || string.IsNullOrWhiteSpace(resolved.ApiKey))
            throw new InvalidOperationException("Automaton server credentials are not configured.");
        server = resolved.Server;
        apiKey = resolved.ApiKey;
        return resolved.AppId;
    }

    private static void Write(JsonElement state, string appId, string? name, bool asJson)
    {
        var paused = state.GetProperty("paused").GetBoolean();
        var changed = state.TryGetProperty("schedulesChanged", out var count) ? count.GetInt32() : 0;
        var warnings = state.TryGetProperty("warnings", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
            : [];
        if (asJson)
        {
            CliJson.Write(new { success = true, appId, name, paused, schedulesChanged = changed, warnings });
            return;
        }
        var label = name ?? appId;
        AnsiConsole.MarkupLine(paused
            ? $"[green]Paused {Markup.Escape(label)}.[/] It is stopped, {changed} schedule(s) are off and its address shows a paused page. Resume it with: automaton resume"
            : $"[green]Resumed {Markup.Escape(label)}.[/] {changed} schedule(s) are on again.");
        foreach (var warning in warnings)
            AnsiConsole.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");
    }

    private static void WriteFailure(Exception exception, bool asJson)
    {
        if (asJson) CliJson.Write(new { success = false, error = exception.Message });
        else AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
    }
}
