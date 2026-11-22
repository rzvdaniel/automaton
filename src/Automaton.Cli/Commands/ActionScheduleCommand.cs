using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Automaton.Cli.Services;
using Cronos;

namespace Automaton.Cli.Commands;

/// <summary><c>automaton action schedule</c>: manage an Action's cron schedules for any app the user may update. Cron is the only timing format.</summary>
public static class ActionScheduleCommand
{
    private const int NextRunCount = 3;

    public static Command Build(ConfigService config)
    {
        var command = new Command("schedule", "Run an Action on a cron schedule (five fields plus a time zone)");
        command.AddCommand(BuildList(config));
        command.AddCommand(BuildAdd(config));
        command.AddCommand(BuildEdit(config));
        command.AddCommand(BuildSwitch(config, on: true));
        command.AddCommand(BuildSwitch(config, on: false));
        command.AddCommand(BuildDelete(config));
        return command;
    }

    private static Command BuildList(ConfigService config)
    {
        var action = new Option<string?>("--action", "Only this Action's schedules");
        var json = new Option<bool>("--json", "Print the schedules as JSON");
        var app = ActionCommand.AppOption();
        var command = new Command("list", "List the app's schedules with their next run") { action, json, app };
        command.SetHandler(async (string? actionKey, bool asJson, string? appRef) => await Guard(async () =>
        {
            var (server, apiKey, appId) = await ActionCommand.ResolveAsync(config, appRef);
            using var client = new AutomatonApiClient(server, apiKey);
            var rows = Visible(await client.ListActionSchedulesAsync(), appId)
                .Where(item => actionKey is null || Text(item, "actionKey") == actionKey).ToList();
            if (asJson) { Console.WriteLine(JsonSerializer.Serialize(rows)); return; }
            Console.WriteLine(Table(rows));
        }), action, json, app);
        return command;
    }

    private static Command BuildAdd(ConfigService config)
    {
        var key = new Argument<string>("action", "The Action's key");
        var (cron, tz, input, name, overlap) = SharedOptions();
        var app = ActionCommand.AppOption();
        cron.IsRequired = true;
        var command = new Command("add", "Create a schedule and print its next runs") { key, cron, tz, input, name, overlap, app };
        command.SetHandler(async (string actionKey, string cronText, string? zone, string? inputJson, string? scheduleName, bool allowOverlap, string? appRef) => await Guard(async () =>
        {
            var timeZone = zone ?? "Etc/UTC";
            var parsed = ParseCron(cronText);
            CheckTimeZone(timeZone);
            var inputValue = ParseInput(inputJson) ?? JsonDocument.Parse("{}").RootElement;
            var (server, apiKey, appId) = await ActionCommand.ResolveAsync(config, appRef);
            using var client = new AutomatonApiClient(server, apiKey);
            var created = await client.CreateActionScheduleAsync(new
            {
                applicationId = appId, actionKey,
                name = string.IsNullOrWhiteSpace(scheduleName) ? actionKey : scheduleName,
                executionScope = "AppRunner", scheduleType = "Cron", cronExpression = cronText.Trim(), timeZone,
                enabled = true, concurrencyPolicy = allowOverlap ? "Allow" : "SkipIfRunning", misfirePolicy = "Skip",
                input = inputValue
            });
            Console.WriteLine($"Created schedule {Short(created)} for {actionKey}: {cronText.Trim()} ({timeZone}).");
            PrintNextRuns(parsed, timeZone);
        }), key, cron, tz, input, name, overlap, app);
        return command;
    }

    private static Command BuildEdit(ConfigService config)
    {
        var id = new Argument<string>("id", "The schedule's ID from the list");
        var (cron, tz, input, name, _) = SharedOptions();
        var overlap = new Option<bool?>("--allow-overlap", "true to let runs overlap, false to skip a run while one is running");
        var app = ActionCommand.AppOption();
        var command = new Command("edit", "Change only the options given and print the next runs") { id, cron, tz, input, name, overlap, app };
        command.SetHandler(async (string idText, string? cronText, string? zone, string? inputJson, string? scheduleName, bool? allowOverlap, string? appRef) => await Guard(async () =>
        {
            if (cronText is null && zone is null && inputJson is null && scheduleName is null && allowOverlap is null)
                throw new InvalidOperationException("Nothing to change: give --cron, --tz, --input, --name or --allow-overlap.");
            var parsed = cronText is null ? null : ParseCron(cronText);
            if (zone is not null) CheckTimeZone(zone);
            var inputValue = ParseInput(inputJson);
            var (server, apiKey, appId) = await Resolve(config, appRef);
            using var client = new AutomatonApiClient(server, apiKey);
            var current = Pick(await client.ListActionSchedulesAsync(), idText, appId);
            var request = BuildEditRequest(current, cronText, zone, inputValue, scheduleName, allowOverlap);
            var saved = await client.UpdateActionScheduleAsync(GuidOf(current), request);
            Console.WriteLine($"Saved schedule {Short(saved)}.");
            var savedCron = Text(saved, "cronExpression");
            if (savedCron.Length > 0) PrintNextRuns(parsed ?? ParseCron(savedCron), Text(saved, "timeZone"));
        }), id, cron, tz, input, name, overlap, app);
        return command;
    }

    private static Command BuildSwitch(ConfigService config, bool on)
    {
        var id = new Argument<string>("id", "The schedule's ID from the list");
        var app = ActionCommand.AppOption();
        var command = new Command(on ? "on" : "off", on ? "Switch a schedule on" : "Switch a schedule off") { id, app };
        command.SetHandler(async (string idText, string? appRef) => await Guard(async () =>
        {
            var (server, apiKey, appId) = await Resolve(config, appRef);
            using var client = new AutomatonApiClient(server, apiKey);
            var current = Pick(await client.ListActionSchedulesAsync(), idText, appId);
            var saved = await client.SetActionScheduleEnabledAsync(GuidOf(current), on);
            var next = Text(saved, "nextRunAtUtc");
            Console.WriteLine(on ? $"Schedule {Short(saved)} is on; next run {Format(next)}." : $"Schedule {Short(saved)} is off.");
        }), id, app);
        return command;
    }

    private static Command BuildDelete(ConfigService config)
    {
        var id = new Argument<string>("id", "The schedule's ID from the list");
        var yes = new Option<bool>("--yes", "Delete without asking");
        var app = ActionCommand.AppOption();
        var command = new Command("delete", "Delete a schedule (asks first)") { id, yes, app };
        command.SetHandler(async (string idText, bool confirmed, string? appRef) => await Guard(async () =>
        {
            var (server, apiKey, appId) = await Resolve(config, appRef);
            using var client = new AutomatonApiClient(server, apiKey);
            var current = Pick(await client.ListActionSchedulesAsync(), idText, appId);
            if (!confirmed)
            {
                if (Console.IsInputRedirected) throw new InvalidOperationException("Pass --yes to delete without being asked.");
                Console.Write($"Delete schedule {Short(current)} ({Text(current, "actionKey")}, {Text(current, "cronExpression")})? [y/N] ");
                if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("Not deleted."); return; }
            }
            await client.DeleteActionScheduleAsync(GuidOf(current));
            Console.WriteLine($"Deleted schedule {Short(current)}.");
        }), id, yes, app);
        return command;
    }

    private static (Option<string> Cron, Option<string?> Tz, Option<string?> Input, Option<string?> Name, Option<bool> Overlap) SharedOptions() => (
        new Option<string>("--cron", "Five-field cron: minute hour day-of-month month day-of-week (no seconds)"),
        new Option<string?>("--tz", "IANA time zone, for example Europe/Bucharest (default UTC)"),
        new Option<string?>("--input", "JSON input for each run"),
        new Option<string?>("--name", "A name for the schedule (default: the Action key)"),
        new Option<bool>("--allow-overlap", "Start a run even while the last one is still running"));

    /// <summary>Id commands work on any schedule the user owns; <c>--app</c> narrows the lookup to one app, otherwise no linked folder is needed.</summary>
    private static async Task<(string Server, string ApiKey, string? AppId)> Resolve(ConfigService config, string? appRef)
    {
        if (!string.IsNullOrWhiteSpace(appRef)) return await ActionCommand.ResolveAsync(config, appRef);
        var (server, apiKey) = config.ResolveCredentials(directory: Directory.GetCurrentDirectory());
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Run automaton login to connect to your server.");
        return (server, apiKey, null);
    }

    internal static CronExpression ParseCron(string text)
    {
        var fields = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5)
            throw new InvalidOperationException($"Cron must have five fields (minute hour day-of-month month day-of-week), not {fields.Length}; there is no seconds field.");
        try { return CronExpression.Parse(string.Join(' ', fields), CronFormat.Standard); }
        catch (CronFormatException exception) { throw new InvalidOperationException($"Not a valid cron expression: {exception.Message}"); }
    }

    internal static TimeZoneInfo CheckTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new InvalidOperationException($"'{id}' is not a supported IANA time zone (for example Europe/Bucharest or UTC)."); }
    }

    internal static List<DateTime> NextRuns(CronExpression cron, string timeZone, int count = NextRunCount)
    {
        var zone = CheckTimeZone(timeZone);
        var runs = new List<DateTime>();
        var cursor = DateTime.UtcNow;
        while (runs.Count < count && cron.GetNextOccurrence(cursor, zone, inclusive: false) is { } next) { runs.Add(next); cursor = next; }
        return runs;
    }

    private static void PrintNextRuns(CronExpression cron, string timeZone)
    {
        var runs = NextRuns(cron, timeZone);
        Console.WriteLine(runs.Count == 0 ? "No future runs." : "Next runs (UTC): " + string.Join(", ", runs.Select(run => run.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))));
    }

    private static JsonElement? ParseInput(string? text)
    {
        if (text is null) return null;
        try { return JsonDocument.Parse(text).RootElement.Clone(); }
        catch (JsonException exception) { throw new InvalidOperationException($"--input is not valid JSON: {exception.Message}"); }
    }

    /// <summary>The full PUT body: the stored schedule with only the given options replaced; giving --cron makes it a cron schedule.</summary>
    internal static Dictionary<string, object?> BuildEditRequest(JsonElement current, string? cron, string? zone, JsonElement? input, string? name, bool? allowOverlap)
    {
        object? Raw(string property) => current.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.Clone() : null;
        var request = new Dictionary<string, object?>
        {
            ["applicationId"] = Raw("applicationId"), ["actionKey"] = Raw("actionKey"), ["name"] = name ?? Text(current, "name"),
            ["description"] = Raw("description"), ["executionScope"] = Raw("executionScope"), ["scheduleType"] = Raw("scheduleType"),
            ["runAtUtc"] = Raw("runAtUtc"), ["intervalSeconds"] = Raw("intervalSeconds"), ["cronExpression"] = Raw("cronExpression"),
            ["timeZone"] = zone ?? Text(current, "timeZone"), ["enabled"] = current.GetProperty("enabled").GetBoolean(),
            ["concurrencyPolicy"] = allowOverlap is null ? Raw("concurrencyPolicy") : allowOverlap.Value ? "Allow" : "SkipIfRunning",
            ["misfirePolicy"] = Raw("misfirePolicy"),
            ["input"] = input ?? ParseStored(Text(current, "inputJson"))
        };
        if (cron is not null)
        {
            request["scheduleType"] = "Cron";
            request["cronExpression"] = cron.Trim();
            request["runAtUtc"] = null;
            request["intervalSeconds"] = null;
        }
        return request;
    }

    private static JsonElement ParseStored(string text) => JsonDocument.Parse(text.Length == 0 ? "{}" : text).RootElement.Clone();

    /// <summary>The user's schedules for one app (home-card schedules are the platform's, not shown).</summary>
    internal static List<JsonElement> Visible(IEnumerable<JsonElement> all, string? appId)
        => all.Where(item => appId is null || string.Equals(Text(item, "applicationId"), appId, StringComparison.OrdinalIgnoreCase)).ToList();

    internal static JsonElement Pick(IEnumerable<JsonElement> all, string id, string? appId)
    {
        var rows = Visible(all, appId);
        return MatchId(rows, id, GuidOf) ?? throw new InvalidOperationException($"No schedule matches '{id}'. Run automaton action schedule list.");
    }

    /// <summary>Finds one item by full ID or by an ID prefix of at least 8 characters; null when none matches, an error when several do.</summary>
    internal static JsonElement? MatchId(IReadOnlyList<JsonElement> items, string text, Func<JsonElement, Guid> idOf)
    {
        var wanted = text.Trim().Replace("-", "").ToLowerInvariant();
        if (wanted.Length < 8 || !wanted.All(Uri.IsHexDigit)) return null;
        var found = items.Where(item => idOf(item).ToString("N").StartsWith(wanted, StringComparison.Ordinal)).ToList();
        if (found.Count > 1) throw new InvalidOperationException($"'{text}' matches {found.Count} schedules; use more of the ID.");
        return found.Count == 1 ? found[0] : null;
    }

    internal static string Table(IReadOnlyList<JsonElement> rows)
    {
        if (rows.Count == 0) return "No schedules.";
        var lines = new List<string[]> { new[] { "ID", "ACTION", "CRON", "TZ", "ON", "NEXT" } };
        foreach (var row in rows)
        {
            var cron = Text(row, "cronExpression");
            var type = Text(row, "scheduleType");
            lines.Add([Short(row), Text(row, "actionKey"), cron.Length > 0 ? cron : $"({type.ToLowerInvariant()})", Text(row, "timeZone"),
                row.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True ? "yes" : "no", Format(Text(row, "nextRunAtUtc"))]);
        }
        var widths = Enumerable.Range(0, 6).Select(column => lines.Max(line => line[column].Length)).ToArray();
        return string.Join(Environment.NewLine, lines.Select(line => "  " + string.Join("  ", line.Select((cell, column) => cell.PadRight(widths[column]))).TrimEnd()));
    }

    private static string Format(string utc)
        => DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "-";

    private static string Text(JsonElement item, string property)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";

    private static Guid GuidOf(JsonElement item) => item.GetProperty("id").GetGuid();
    private static string Short(JsonElement item) => GuidOf(item).ToString("N")[..8];

    private static async Task Guard(Func<Task> work)
    {
        try { await work(); }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Error: {ActionCommand.ApiErrorText(exception.Message)}");
            Environment.ExitCode = 1;
        }
    }
}
