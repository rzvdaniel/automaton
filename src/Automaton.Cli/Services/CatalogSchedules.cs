using System.Text.Json;

namespace Automaton.Cli.Services;

/// <summary>The default schedules an app's install manifest offers: which ones to create, and creating them like Studio's install page does.</summary>
public static class CatalogSchedules
{
    /// <summary>The manifest's schedules, narrowed by --no-schedules (none) and --schedule (only those names). An unknown name is refused.</summary>
    public static List<JsonElement> Select(JsonElement application, bool none, string[] names)
    {
        var offered = application.ValueKind == JsonValueKind.Object && application.TryGetProperty("schedules", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(item => item.Clone()).ToList() : [];
        if (none && names.Length > 0) throw new InvalidOperationException("Use either --no-schedules or --schedule, not both. Nothing was changed.");
        if (none) return [];
        if (names.Length == 0) return offered;
        var unknown = names.Where(name => !offered.Any(item => Matches(item, name))).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"The app declares no schedule named: {string.Join(", ", unknown)}. It offers: {(offered.Count == 0 ? "none" : string.Join(", ", offered.Select(Name)))}. Nothing was changed.");
        return offered.Where(item => names.Any(name => Matches(item, name))).ToList();
    }

    public static string Name(JsonElement item) => item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";

    private static bool Matches(JsonElement item, string name) => string.Equals(Name(item), name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates each chosen schedule the app does not have yet (named "App name: schedule name", as Studio does). Returns the names created and the ones that were already there.</summary>
    public static async Task<(List<string> Created, List<string> Existing)> CreateAsync(AutomatonApiClient client, string appId, string appName, IEnumerable<JsonElement> chosen)
    {
        var have = (await client.ListActionSchedulesAsync()).Where(item => string.Equals(Str(item, "applicationId"), appId, StringComparison.OrdinalIgnoreCase))
            .Select(item => Str(item, "name")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> created = [], existing = [];
        foreach (var item in chosen)
        {
            var name = Name(item);
            var full = $"{appName.Trim()}: {name}";
            if (have.Contains(full)) { existing.Add(name); continue; }
            await client.CreateActionScheduleAsync(Body(appId, full, item, null));
            created.Add(name);
        }
        return (created, existing);
    }

    /// <summary>The body Studio posts for a manifest schedule; <paramref name="current"/> (an existing schedule) keeps its on/off state and policies.</summary>
    internal static object Body(string appId, string fullName, JsonElement item, JsonElement? current)
    {
        var cron = Str(item, "cron");
        return new
        {
            applicationId = appId, actionKey = Str(item, "action"), name = fullName,
            description = Str(item, "description") is { Length: > 0 } text ? text : null,
            executionScope = "AppRunner", scheduleType = cron.Length > 0 ? "Cron" : "Interval",
            cronExpression = cron.Length > 0 ? cron : null,
            intervalSeconds = cron.Length > 0 || !item.TryGetProperty("intervalSeconds", out var seconds) || seconds.ValueKind != JsonValueKind.Number ? (int?)null : seconds.GetInt32(),
            timeZone = Str(item, "timeZone") is { Length: > 0 } zone ? zone : "Etc/UTC",
            enabled = current is null || Str(current.Value, "enabled") == "True",
            concurrencyPolicy = Kept(current, "concurrencyPolicy", "SkipIfRunning"),
            misfirePolicy = Kept(current, "misfirePolicy", "Skip"),
            input = item.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object ? input : JsonDocument.Parse("{}").RootElement
        };
    }

    private static string Kept(JsonElement? current, string property, string fallback) => current is { } item && Str(item, property) is { Length: > 0 } value ? value : fallback;

    internal static string Str(JsonElement item, string property) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.ToString() : "";
}
