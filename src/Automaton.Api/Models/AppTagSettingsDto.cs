using System.Text.Json;
using System.Text.Json.Serialization;

namespace Automaton.Api.Models;

public sealed class AppTagSettingsDto
{
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

public sealed class UpdateAppTagSettingsRequest
{
    [JsonConverter(typeof(StringOrStringArrayConverter))]
    public IReadOnlyList<string>? Tags { get; init; }
}

public sealed class StringOrStringArrayConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return Array.Empty<string>();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return [reader.GetString() ?? string.Empty];
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Tags must be a string or an array of strings.");
        }

        var tags = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Tags must contain only strings.");
            }

            tags.Add(reader.GetString() ?? string.Empty);
        }

        return tags;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, options);
}
