using System.Text.Json;

namespace Automaton.Api.Services;

public sealed class SecretValueSanitizer
{
    public static SecretValueSanitizer Empty { get; } = new([]);

    private readonly IReadOnlyList<string> representations;

    public SecretValueSanitizer(IEnumerable<string> values)
    {
        representations = values
            .Where(value => !string.IsNullOrEmpty(value))
            .SelectMany(value =>
            {
                var encoded = JsonSerializer.Serialize(value);
                return encoded.Length > 2 ? new[] { value, encoded[1..^1] } : new[] { value };
            })
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(value => value.Length)
            .ToArray();
    }

    public string Sanitize(string? value)
    {
        var sanitized = value ?? string.Empty;
        foreach (var representation in representations)
            sanitized = sanitized.Replace(representation, "[REDACTED]", StringComparison.Ordinal);
        return sanitized;
    }
}
