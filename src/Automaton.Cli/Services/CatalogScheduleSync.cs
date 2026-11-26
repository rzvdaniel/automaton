using System.Text.Json;

namespace Automaton.Cli.Services;

/// <summary>One planned change to an app's default schedules when it updates to a new manifest.</summary>
public sealed record ScheduleChange(string Kind, string Name, string Detail, JsonElement? Existing, JsonElement? Declaration)
{
    public const string Add = "add", Update = "update", Remove = "remove", Keep = "keep";
}

/// <summary>
/// Brings an app's default schedules up to the new manifest. A schedule counts as an unchanged default when it still equals what the
/// previous manifest declared; those are updated, renamed or removed, while schedules someone edited or created are kept and listed.
/// </summary>
public static class CatalogScheduleSync
{
    public static string FullName(string appName, string name) => $"{appName.Trim()}: {name}";

    /// <summary>Plans the changes. <paramref name="previous"/> null means the old manifest is unknown: then nothing existing is touched, missing schedules are only added.</summary>
    public static List<ScheduleChange> Plan(string appName, IReadOnlyList<JsonElement> existing, IReadOnlyList<JsonElement>? previous,
        IReadOnlyList<JsonElement> chosen, IReadOnlyList<JsonElement> all, bool allowRemove)
    {
        var plan = new List<ScheduleChange>();
        JsonElement? Have(string name) => existing.Where(item => string.Equals(CatalogSchedules.Str(item, "name"), FullName(appName, name), StringComparison.OrdinalIgnoreCase))
            .Select(item => (JsonElement?)item).FirstOrDefault();
        JsonElement? Old(string name) => previous?.Where(item => string.Equals(CatalogSchedules.Name(item), name, StringComparison.OrdinalIgnoreCase)).Select(item => (JsonElement?)item).FirstOrDefault();
        bool InNew(string name) => all.Any(item => string.Equals(CatalogSchedules.Name(item), name, StringComparison.OrdinalIgnoreCase));

        // Old defaults the manifest dropped and that nobody changed.
        var dropped = previous is null ? [] : previous.Where(item => !InNew(CatalogSchedules.Name(item))).ToList();
        var droppedUnchanged = dropped.Where(item => Have(CatalogSchedules.Name(item)) is { } have && Same(have, item)).ToList();
        var missingNew = chosen.Where(item => Have(CatalogSchedules.Name(item)) is null).ToList();
        // Existing schedules that do the same as a declaration (action, cron, time zone, input), whatever their name, and are not
        // claimed by a declaration of that name.
        bool HasEquivalent(JsonElement declared) => existing.Any(item => Same(item, declared) && !all.Any(other => string.Equals(FullName(appName, CatalogSchedules.Name(other)), CatalogSchedules.Str(item, "name"), StringComparison.OrdinalIgnoreCase)));
        var (renamed, renamedTo) = PlanRenames(plan, droppedUnchanged, missingNew, Have);

        foreach (var item in chosen)
        {
            var name = CatalogSchedules.Name(item);
            if (renamedTo.Contains(item)) continue;
            if (Have(name) is not { } have)
            {
                if (HasEquivalent(item)) continue; // the app already has this schedule under another name
                plan.Add(new ScheduleChange(ScheduleChange.Add, name, "new", null, item));
                continue;
            }
            if (Same(have, item)) continue;
            if (Old(name) is { } old && Same(have, old)) plan.Add(new ScheduleChange(ScheduleChange.Update, name, Diff(old, item, includeName: false).TrimStart(';', ' '), have, item));
            else plan.Add(new ScheduleChange(ScheduleChange.Keep, name, previous is null ? "kept as it is (the previous manifest is unknown)" : "kept: it was changed after the install", have, item));
        }

        if (allowRemove) PlanRemovals(plan, dropped.Where(item => !renamed.Contains(item)), Have);
        return plan;
    }

    /// <summary>A dropped unchanged default and a new schedule for the same action, one each, are a rename: the existing schedule is updated in place.</summary>
    private static (HashSet<JsonElement> From, HashSet<JsonElement> To) PlanRenames(List<ScheduleChange> plan, List<JsonElement> droppedUnchanged, List<JsonElement> missingNew, Func<string, JsonElement?> have)
    {
        HashSet<JsonElement> renamed = [], renamedTo = [];
        foreach (var group in droppedUnchanged.GroupBy(item => CatalogSchedules.Str(item, "action")))
        {
            var targets = missingNew.Where(item => CatalogSchedules.Str(item, "action") == group.Key).ToList();
            if (group.Count() != 1 || targets.Count != 1) continue;
            var from = group.First(); var to = targets[0];
            plan.Add(new ScheduleChange(ScheduleChange.Update, CatalogSchedules.Name(to), $"rename {CatalogSchedules.Name(from)} → {CatalogSchedules.Name(to)}{Diff(from, to, includeName: false)}", have(CatalogSchedules.Name(from)), to));
            renamed.Add(from); renamedTo.Add(to);
        }
        return (renamed, renamedTo);
    }

    private static void PlanRemovals(List<ScheduleChange> plan, IEnumerable<JsonElement> dropped, Func<string, JsonElement?> have)
    {
        foreach (var item in dropped)
        {
            var name = CatalogSchedules.Name(item);
            if (have(name) is not { } existing) continue;
            plan.Add(Same(existing, item)
                ? new ScheduleChange(ScheduleChange.Remove, name, "the new version no longer declares it", existing, null)
                : new ScheduleChange(ScheduleChange.Keep, name, "kept: it was changed after the install and the new version no longer declares it", existing, null));
        }
    }

    /// <summary>Applies the plan after the deployment; returns the names created, updated and removed.</summary>
    public static async Task<(List<string> Created, List<string> Updated, List<string> Removed)> ApplyAsync(AutomatonApiClient client, string appId, string appName, IEnumerable<ScheduleChange> plan)
    {
        List<string> created = [], updated = [], removed = [];
        foreach (var change in plan)
        {
            switch (change.Kind)
            {
                case ScheduleChange.Add:
                    await client.CreateActionScheduleAsync(CatalogSchedules.Body(appId, FullName(appName, change.Name), change.Declaration!.Value, null));
                    created.Add(change.Name); break;
                case ScheduleChange.Update:
                    await client.UpdateActionScheduleAsync(Guid.Parse(CatalogSchedules.Str(change.Existing!.Value, "id")),
                        CatalogSchedules.Body(appId, FullName(appName, change.Name), change.Declaration!.Value, change.Existing));
                    updated.Add(change.Name); break;
                case ScheduleChange.Remove:
                    await client.DeleteActionScheduleAsync(Guid.Parse(CatalogSchedules.Str(change.Existing!.Value, "id")));
                    removed.Add(change.Name); break;
            }
        }
        return (created, updated, removed);
    }

    /// <summary>The manifest's schedules for the app, or null when that version's manifest cannot be read.</summary>
    public static async Task<List<JsonElement>?> ReadDeclaredAsync(AutomatonApiClient client, string repo, string folder, string commit)
    {
        try
        {
            var source = await client.GetCatalogInstallSourceAsync(repo, folder, commit);
            return CatalogSchedules.Select(source.TryGetProperty("application", out var contract) ? contract : default, false, []);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }

    public static List<JsonElement> ForApp(IEnumerable<JsonElement> all, string appId)
        => all.Where(item => string.Equals(CatalogSchedules.Str(item, "applicationId"), appId, StringComparison.OrdinalIgnoreCase)).ToList();

    public static string Describe(ScheduleChange change) => change.Kind switch
    {
        ScheduleChange.Add => $"add {change.Name}",
        ScheduleChange.Update => change.Detail.StartsWith("rename ", StringComparison.Ordinal) ? change.Detail : $"update {change.Name} ({change.Detail})",
        ScheduleChange.Remove => $"remove {change.Name} ({change.Detail})",
        _ => $"{change.Name} {change.Detail}"
    };

    /// <summary>True when an existing schedule has the declaration's action, timing and input.</summary>
    private static bool Same(JsonElement existing, JsonElement declared)
    {
        var cron = CatalogSchedules.Str(declared, "cron");
        return Eq(CatalogSchedules.Str(existing, "actionKey"), CatalogSchedules.Str(declared, "action"))
            && Eq(CatalogSchedules.Str(existing, "cronExpression"), cron)
            && (cron.Length > 0 || Eq(CatalogSchedules.Str(existing, "intervalSeconds"), CatalogSchedules.Str(declared, "intervalSeconds")))
            && Eq(Zone(existing), Zone(declared))
            && Canon(StoredInput(existing)) == Canon(DeclaredInput(declared));
    }

    private static string Diff(JsonElement from, JsonElement to, bool includeName)
    {
        var parts = new List<string>();
        string Cron(JsonElement item) => CatalogSchedules.Str(item, "cron") is { Length: > 0 } cron ? cron : CatalogSchedules.Str(item, "cronExpression") is { Length: > 0 } stored ? stored
            : $"every {(CatalogSchedules.Str(item, "intervalSeconds") is { Length: > 0 } seconds ? seconds : "?")} s";
        if (Cron(from) != Cron(to)) parts.Add($"{Cron(from)} → {Cron(to)}");
        if (Zone(from) != Zone(to)) parts.Add($"time zone {Zone(from)} → {Zone(to)}");
        if (!Eq(CatalogSchedules.Str(from, "action"), CatalogSchedules.Str(to, "action")) && CatalogSchedules.Str(from, "action").Length > 0) parts.Add($"action {CatalogSchedules.Str(from, "action")} → {CatalogSchedules.Str(to, "action")}");
        var (oldInput, newInput) = (Canon(DeclaredInput(from)), Canon(DeclaredInput(to)));
        if (oldInput != newInput) parts.Add(oldInput == "{}" ? "input added" : newInput == "{}" ? "input removed" : "input changed");
        return parts.Count == 0 ? "" : $"{(includeName ? "" : "; ")}{string.Join("; ", parts)}";
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string Zone(JsonElement item) => (CatalogSchedules.Str(item, "timeZone") is { Length: > 0 } zone ? zone : "Etc/UTC");

    private static JsonElement StoredInput(JsonElement existing)
    {
        var text = CatalogSchedules.Str(existing, "inputJson");
        try { return JsonDocument.Parse(text.Length == 0 ? "{}" : text).RootElement.Clone(); } catch (JsonException) { return JsonDocument.Parse("{}").RootElement.Clone(); }
    }

    private static JsonElement DeclaredInput(JsonElement declared)
        => declared.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object ? input : JsonDocument.Parse("{}").RootElement.Clone();

    private static string Canon(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => $"{JsonSerializer.Serialize(p.Name)}:{Canon(p.Value)}")) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canon)) + "]",
        _ => value.GetRawText()
    };
}
