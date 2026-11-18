using System.Globalization;
using System.Text.Json;

namespace Automaton.Core.Models;

/// <summary>The outcome of checking a card: the normalized card when there are no errors, plus everything wrong with it.</summary>
public sealed record AppHomeCardCheck(AppHomeCardContent? Card, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>Reads one object of a card and records every problem with its path, so authors see what to fix.</summary>
internal sealed class CardNode(JsonElement element, string path, List<string> errors)
{
    public string Path => path;
    public int ErrorCount => errors.Count;
    public void Error(string message) => errors.Add($"{path}.{message}");

    public string? Text(string name, int max, bool required = false)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required) Error($"{name} is required");
            return null;
        }
        if (value.ValueKind != JsonValueKind.String) { Error($"{name} must be text"); return null; }
        var text = value.GetString()!.Trim();
        if (text.Length > max) Error($"{name} is longer than {max} characters");
        else if (text.Any(char.IsControl)) Error($"{name} has control characters");
        else if (text.Length > 0) return text;
        else if (required) Error($"{name} is empty");
        return null;
    }

    public string? Tone(string name = "tone", bool required = false)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required) Error($"{name} is required");
            return null;
        }
        if (value.ValueKind == JsonValueKind.String && AppHomeCardLimits.Tones.Contains(value.GetString())) return value.GetString();
        Error($"{name} must be one of {string.Join(", ", AppHomeCardLimits.Tones)} (tones only, never colours)");
        return null;
    }

    public double? Number(string name, bool required = false)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required) Error($"{name} must be a number");
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)) return number;
        Error($"{name} must be a number");
        return null;
    }

    /// <summary>Visits the objects of a list property; reports a wrong type or too many entries.</summary>
    public void Each(string name, int max, Action<CardNode> visit)
    {
        if (!element.TryGetProperty(name, out var list) || list.ValueKind == JsonValueKind.Null) return;
        if (list.ValueKind != JsonValueKind.Array) { Error($"{name} must be a list"); return; }
        if (list.GetArrayLength() > max) { Error($"{name} can have at most {max} entries"); return; }
        var index = 0;
        foreach (var item in list.EnumerateArray())
        {
            var child = new CardNode(item, $"{path}.{name}[{index++}]", errors);
            if (item.ValueKind != JsonValueKind.Object) errors.Add($"{child.Path} must be an object");
            else visit(child);
        }
    }

    /// <summary>Reads a list of plain values with one converter; null from the converter marks an invalid entry.</summary>
    public List<T> Values<T>(string name, int max, Func<JsonElement, T?> read) where T : struct
    {
        var result = new List<T>();
        if (!element.TryGetProperty(name, out var list) || list.ValueKind == JsonValueKind.Null) return result;
        if (list.ValueKind != JsonValueKind.Array) { Error($"{name} must be a list"); return result; }
        if (list.GetArrayLength() > max) { Error($"{name} can have at most {max} entries"); return result; }
        foreach (var item in list.EnumerateArray())
        {
            if (read(item) is { } value) result.Add(value);
            else Error($"{name} has an entry that is not valid");
        }
        return result;
    }

    public List<string> Texts(string name, int max, int maxLength)
    {
        var result = new List<string>();
        if (!element.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array) return result;
        if (list.GetArrayLength() > max) { Error($"{name} can have at most {max} entries"); return result; }
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString()!.Length <= maxLength && !item.GetString()!.Any(char.IsControl)) result.Add(item.GetString()!);
            else Error($"{name} has an entry that is not text of {maxLength} characters or fewer");
        }
        return result;
    }
}

public static class AppHomeCardValidator
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Validates a card's JSON. Returns null for anything invalid.</summary>
    public static AppHomeCardContent? TryParse(string? json) => Check(json).Card;

    public static string Serialize(AppHomeCardContent card) => JsonSerializer.Serialize(card, Options);

    public static AppHomeCardContent? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<AppHomeCardContent>(json, Options); }
        catch (JsonException) { return null; }
    }

    /// <summary>The first problems of a failed check as one line, for the stored reason.</summary>
    public static string Reason(IEnumerable<string> errors)
    {
        var text = string.Join("; ", errors.Take(3));
        return text.Length > 300 ? text[..300] : text;
    }

    /// <summary>Checks a card and reports every problem. An unknown block type is skipped with a warning; any other defect makes the card invalid.</summary>
    public static AppHomeCardCheck Check(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Failed("the card is empty");
        if (json == AppHomeCardLimits.TooLarge || json.Length > AppHomeCardLimits.MaxJsonBytes)
            return Failed($"the card is larger than {AppHomeCardLimits.MaxJsonBytes / 1024} KB");
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return Failed("the card must be a JSON object");
            return CheckCard(document.RootElement);
        }
        catch (JsonException exception)
        {
            return Failed($"not valid JSON ({exception.Message.Split('.')[0]})");
        }
    }

    private static AppHomeCardCheck Failed(string reason) => new(null, [reason], []);

    private static AppHomeCardCheck CheckCard(JsonElement root)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var node = new CardNode(root, "card", errors);
        var status = node.Text("status", AppHomeCardLimits.MaxStatus, required: true);
        var detail = node.Text("detail", AppHomeCardLimits.MaxDetail);
        var tone = node.Tone();
        var facts = new List<AppHomeCardFact>();
        node.Each("facts", AppHomeCardLimits.MaxFacts, fact =>
        {
            var label = fact.Text("label", AppHomeCardLimits.MaxLabel, required: true);
            var value = fact.Text("value", AppHomeCardLimits.MaxValue);
            if (label is not null) facts.Add(new(label, value ?? string.Empty));
        });
        var alerts = new List<AppHomeCardAlert>();
        node.Each("alerts", AppHomeCardLimits.MaxAlerts, alert => AddAlert(alert, alerts));
        var blocks = new List<Dictionary<string, object?>>();
        node.Each("blocks", AppHomeCardLimits.MaxBlocks, block =>
        {
            var type = block.Text("type", 16, required: true);
            if (type == "alert") AddAlert(block, alerts);
            else if (type is not null && ReadBlock(type, block) is { } read) blocks.Add(read);
            else if (type is not null && !AppHomeCardBlocks.Known.Contains(type)) warnings.Add($"{block.Path}: unknown block type '{type}' is skipped");
        });
        if (alerts.Count > AppHomeCardLimits.MaxAlerts) errors.Add($"card.alerts and alert blocks together can be at most {AppHomeCardLimits.MaxAlerts}");
        if (errors.Count > 0 || status is null) return new(null, errors, warnings);
        return new(new AppHomeCardContent(status, facts, alerts) { Detail = detail, Tone = tone, Blocks = blocks }, errors, warnings);
    }

    private static void AddAlert(CardNode node, List<AppHomeCardAlert> alerts)
    {
        var severity = node.Text("severity", 16, required: true);
        var text = node.Text("text", AppHomeCardLimits.MaxAlertText, required: true);
        if (severity is null || text is null) return;
        if (AppHomeCardLimits.Severities.Contains(severity)) alerts.Add(new(severity, text));
        else node.Error("severity must be info, warning or critical");
    }

    private static Dictionary<string, object?>? ReadBlock(string type, CardNode node)
    {
        var before = node.ErrorCount;
        var block = new Dictionary<string, object?> { ["type"] = type };
        switch (type)
        {
            case "fact": AppHomeCardBlocks.Fact(node, block); break;
            case "badge": AppHomeCardBlocks.Badge(node, block); break;
            case "meter": AppHomeCardBlocks.Meter(node, block); break;
            case "counters": AppHomeCardBlocks.Counters(node, block); break;
            case "list": AppHomeCardBlocks.List(node, block); break;
            case "buttons": AppHomeCardBlocks.Buttons(node, block); break;
            case "chart": AppHomeCardBlocks.Chart(node, block); break;
            default: return null;
        }
        return node.ErrorCount == before ? block : null;
    }
}

internal static class AppHomeCardBlocks
{
    public static readonly string[] Known = ["fact", "badge", "meter", "counters", "list", "alert", "buttons", "chart"];

    private static void Put(Dictionary<string, object?> block, string name, object? value)
    {
        if (value is not null) block[name] = value;
    }

    public static void Fact(CardNode node, Dictionary<string, object?> block)
    {
        Put(block, "label", node.Text("label", AppHomeCardLimits.MaxLabel, true));
        Put(block, "value", node.Text("value", AppHomeCardLimits.MaxValue, true));
        Put(block, "note", node.Text("note", AppHomeCardLimits.MaxValue));
    }

    public static void Badge(CardNode node, Dictionary<string, object?> block)
    {
        Put(block, "label", node.Text("label", AppHomeCardLimits.MaxLabel, true));
        Put(block, "text", node.Text("text", AppHomeCardLimits.MaxLabel, true));
        Put(block, "tone", node.Tone(required: true));
        Put(block, "note", node.Text("note", AppHomeCardLimits.MaxValue));
    }

    public static void Meter(CardNode node, Dictionary<string, object?> block)
    {
        Put(block, "label", node.Text("label", AppHomeCardLimits.MaxLabel, true));
        Put(block, "unit", node.Text("unit", 12));
        Put(block, "note", node.Text("note", AppHomeCardLimits.MaxValue));
        var value = node.Number("value", true);
        var max = node.Number("max", true);
        var warn = node.Number("warnAt");
        var crit = node.Number("critAt");
        Put(block, "value", value); Put(block, "max", max); Put(block, "warnAt", warn); Put(block, "critAt", crit);
        if (max <= 0) node.Error("max must be greater than 0");
        if (value < 0) node.Error("value cannot be negative");
        if (warn > crit) node.Error("warnAt cannot be above critAt");
        if (warn > max || crit > max) node.Error("warnAt and critAt cannot be above max");
    }

    public static void Counters(CardNode node, Dictionary<string, object?> block)
    {
        var items = new List<Dictionary<string, object?>>();
        node.Each("items", 4, counter =>
        {
            var label = counter.Text("label", AppHomeCardLimits.MaxLabel, true);
            var value = CounterValue(counter);
            var tone = counter.Tone();
            if (label is null || value is null) return;
            var entry = new Dictionary<string, object?> { ["label"] = label, ["value"] = value };
            Put(entry, "tone", tone);
            items.Add(entry);
        });
        if (items.Count < 2 && !HasErrors(node, items.Count)) node.Error("items needs 2 to 4 counters");
        block["items"] = items;
    }

    // A wrong list already reported its own error; only an honest short list needs this one.
    private static bool HasErrors(CardNode node, int read) => read > 0 && node.ErrorCount > 0;

    private static string? CounterValue(CardNode counter)
    {
        var number = counter.Number("value");
        if (number is { } n) return n.ToString("0.##", CultureInfo.InvariantCulture);
        return counter.Text("value", 16, true);
    }

    public static void List(CardNode node, Dictionary<string, object?> block)
    {
        var rows = new List<Dictionary<string, object?>>();
        node.Each("rows", 5, row =>
        {
            var text = row.Text("text", AppHomeCardLimits.MaxValue, true);
            var entry = new Dictionary<string, object?>();
            Put(entry, "text", text);
            Put(entry, "note", row.Text("note", AppHomeCardLimits.MaxValue));
            Put(entry, "tone", row.Tone());
            if (text is not null) rows.Add(entry);
        });
        if (rows.Count == 0) node.Error("rows needs 1 to 5 rows");
        block["rows"] = rows;
    }

    public static void Buttons(CardNode node, Dictionary<string, object?> block)
    {
        var items = new List<Dictionary<string, object?>>();
        node.Each("items", 2, button =>
        {
            var label = button.Text("label", AppHomeCardLimits.MaxLabel, true);
            var path = button.Text("path", 200, true);
            if (path is not null && !IsOwnPath(path)) button.Error("path must be a page of the app that starts with / (no links to other sites)");
            else if (label is not null && path is not null) items.Add(new() { ["label"] = label, ["path"] = path });
        });
        if (items.Count == 0) node.Error("items needs 1 or 2 buttons");
        block["items"] = items;
    }

    private static bool IsOwnPath(string path)
        => path[0] == '/' && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains("..", StringComparison.Ordinal)
            && path.All(c => c != '\\' && !char.IsWhiteSpace(c) && !char.IsControl(c));

    public static void Chart(CardNode node, Dictionary<string, object?> block)
    {
        var kind = node.Text("kind", 16, true);
        if (kind is not null && !AppHomeCardLimits.ChartKinds.Contains(kind))
        {
            node.Error($"kind must be one of {string.Join(", ", AppHomeCardLimits.ChartKinds)}");
            return;
        }
        Put(block, "kind", kind);
        Put(block, "title", node.Text("title", AppHomeCardLimits.MaxLabel));
        var single = kind is "donut" or "gauge";
        var labels = node.Texts("labels", AppHomeCardLimits.MaxChartPoints, 24);
        if (kind is not null && kind != "gauge" && labels.Count == 0) node.Error("labels needs at least one label");
        block["labels"] = labels;
        var series = new List<Dictionary<string, object?>>();
        node.Each("series", single ? 1 : 4, entry => Series(entry, kind, labels.Count, series));
        if (series.Count == 0) node.Error("series needs at least one series");
        block["series"] = series;
        if (kind == "gauge") block["max"] = node.Number("max") is > 0 and { } max ? max : 100d;
    }

    private static void Series(CardNode entry, string? kind, int labelCount, List<Dictionary<string, object?>> series)
    {
        var name = entry.Text("name", AppHomeCardLimits.MaxLabel, true);
        var tone = entry.Tone() ?? "info";
        var values = entry.Values<double>("values", AppHomeCardLimits.MaxChartPoints,
            v => v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null);
        var wanted = kind == "gauge" ? 1 : labelCount;
        if (values.Count != wanted) entry.Error($"values needs exactly {wanted} number{(wanted == 1 ? string.Empty : "s")}, one per label");
        if (name is not null) series.Add(new() { ["name"] = name, ["tone"] = tone, ["values"] = values });
    }
}
