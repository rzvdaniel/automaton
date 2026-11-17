using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Api.Home;

public sealed record AppHomeCardFact(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("value")] string Value);

public sealed record AppHomeCardAlert(
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("text")] string Text);

/// <summary>The card an app shows on Studio Home. Plain text only; Studio never renders it as HTML.</summary>
public sealed record AppHomeCardContent(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("facts")] IReadOnlyList<AppHomeCardFact> Facts,
    [property: JsonPropertyName("alerts")] IReadOnlyList<AppHomeCardAlert> Alerts);

/// <summary>A stored card for Home. <c>Stale</c> means its last run failed or it was not refreshed in time.</summary>
public sealed record AppHomeCardDto(AppHomeCardContent Card, DateTime UpdatedAtUtc, bool Stale);

public static class AppHomeCardLimits
{
    public const string ActionKey = "home-card";
    public const int MaxJsonBytes = 8192;
    public const int MaxStatus = 80;
    public const int MaxFacts = 3;
    public const int MaxLabel = 40;
    public const int MaxValue = 120;
    public const int MaxAlerts = 5;
    public const int MaxAlertText = 240;
    public const int DefaultIntervalSeconds = 900;
    public const int MinimumIntervalSeconds = 300;
    /// <summary>Marker the Runner sends when a card is larger than <see cref="MaxJsonBytes"/>; it never validates.</summary>
    public const string TooLarge = "!too-large";
    public static readonly string[] Severities = ["info", "warning", "critical"];
}

public static class AppHomeCardValidator
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Validates a card's JSON. Returns null for anything invalid, so the previous card stays.</summary>
    public static AppHomeCardContent? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > AppHomeCardLimits.MaxJsonBytes) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var status = ReadText(root, "status", AppHomeCardLimits.MaxStatus, required: true);
            if (status is null) return null;
            var facts = new List<AppHomeCardFact>();
            if (!ReadList(root, "facts", AppHomeCardLimits.MaxFacts, item =>
                {
                    var label = ReadText(item, "label", AppHomeCardLimits.MaxLabel, required: true);
                    var value = ReadText(item, "value", AppHomeCardLimits.MaxValue, required: false);
                    if (label is null || value is null) return false;
                    facts.Add(new(label, value));
                    return true;
                })) return null;
            var alerts = new List<AppHomeCardAlert>();
            if (!ReadList(root, "alerts", AppHomeCardLimits.MaxAlerts, item =>
                {
                    var severity = ReadText(item, "severity", 16, required: true);
                    var text = ReadText(item, "text", AppHomeCardLimits.MaxAlertText, required: true);
                    if (severity is null || text is null || !AppHomeCardLimits.Severities.Contains(severity)) return false;
                    alerts.Add(new(severity, text));
                    return true;
                })) return null;
            return new(status, facts, alerts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Serialize(AppHomeCardContent card) => JsonSerializer.Serialize(card, Options);

    public static AppHomeCardContent? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<AppHomeCardContent>(json, Options); }
        catch (JsonException) { return null; }
    }

    private static bool ReadList(JsonElement root, string name, int max, Func<JsonElement, bool> add)
    {
        if (!root.TryGetProperty(name, out var list) || list.ValueKind == JsonValueKind.Null) return true;
        if (list.ValueKind != JsonValueKind.Array || list.GetArrayLength() > max) return false;
        foreach (var item in list.EnumerateArray())
            if (item.ValueKind != JsonValueKind.Object || !add(item)) return false;
        return true;
    }

    /// <summary>Plain text: a string within the limit, without control characters. Null means invalid.</summary>
    private static string? ReadText(JsonElement element, string name, int max, bool required)
    {
        if (!element.TryGetProperty(name, out var value)) return required ? null : string.Empty;
        if (value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()!.Trim();
        if (text.Length > max || text.Any(char.IsControl) || required && text.Length == 0) return null;
        return text;
    }
}
