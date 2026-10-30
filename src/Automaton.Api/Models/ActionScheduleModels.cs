using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Automaton.Api.Models;

public sealed class SaveActionScheduleRequest
{
    [JsonRequired]
    public Guid ApplicationId { get; set; }
    public string ActionKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ExecutionScope { get; set; } = "AppRunner";
    public string ScheduleType { get; set; } = "Interval";
    [JsonConverter(typeof(ExplicitOffsetDateTimeConverter))]
    public DateTimeOffset? RunAtUtc { get; set; }
    public int? IntervalSeconds { get; set; }
    public string? CronExpression { get; set; }
    public string TimeZone { get; set; } = "Etc/UTC";
    public bool Enabled { get; set; } = true;
    public string ConcurrencyPolicy { get; set; } = "SkipIfRunning";
    public string MisfirePolicy { get; set; } = "Skip";
    [JsonRequired]
    public JsonElement Input { get; set; }
}

public sealed class ExplicitOffsetDateTimeConverter : JsonConverter<DateTimeOffset?>
{
    private static readonly Regex ExplicitOffset = new(
        @"(?:[zZ]|[+-]\d{2}:\d{2})$",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("runAtUtc must be an ISO 8601 timestamp.");
        var value = reader.GetString();
        if (string.IsNullOrWhiteSpace(value) || !ExplicitOffset.IsMatch(value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            throw new JsonException("runAtUtc must include Z or an explicit UTC offset.");
        return parsed;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteStringValue(value.Value);
        else writer.WriteNullValue();
    }
}
