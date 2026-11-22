using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Core.Models;

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
    [property: JsonPropertyName("alerts")] IReadOnlyList<AppHomeCardAlert> Alerts)
{
    /// <summary>One line under the status.</summary>
    [JsonPropertyName("detail")] public string? Detail { get; init; }
    /// <summary>ok, warning, critical or info: colours the card's top edge. Never a colour.</summary>
    [JsonPropertyName("tone")] public string? Tone { get; init; }
    /// <summary>Validated blocks (fact, badge, meter, counters, list, buttons, chart). Each is a plain object with known fields only.</summary>
    [JsonPropertyName("blocks")] public IReadOnlyList<Dictionary<string, object?>> Blocks { get; init; } = [];
}

/// <summary>A stored card for Home. <c>Stale</c> means its last run failed or it was not refreshed in time.</summary>
public sealed record AppHomeCardDto(AppHomeCardContent Card, DateTime UpdatedAtUtc, bool Stale);

public static class AppHomeCardLimits
{
    public const string ActionKey = "home-card";
    public const int MaxJsonBytes = 16384;
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
    public const int MaxBlocks = 6;
    public const int MaxChartPoints = 48;
    public const int MaxDetail = 240;
    public static readonly string[] Tones = ["ok", "warning", "critical", "info"];
    public static readonly string[] ChartKinds = ["line", "area", "bar", "stacked-bar", "donut", "gauge"];
    public static readonly string[] Severities = ["info", "warning", "critical"];
}
