namespace Automaton.Api.Services;

public static class AppTagNormalizer
{
    public const int MaximumTagsPerApp = 20;
    public const int MaximumTagLength = 40;

    public static IReadOnlyList<string> Normalize(string? tags) =>
        Normalize(tags?.Split(',') ?? Array.Empty<string>());

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? tags)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawTag in tags ?? Array.Empty<string>())
        {
            foreach (var value in rawTag.Split(',', StringSplitOptions.None))
            {
                var tag = value.Trim();
                if (tag.Length == 0 || !seen.Add(tag)) continue;
                if (tag.Length > MaximumTagLength)
                    throw new InvalidOperationException($"Tags cannot exceed {MaximumTagLength} characters.");
                normalized.Add(tag);
                if (normalized.Count > MaximumTagsPerApp)
                    throw new InvalidOperationException($"Apps cannot have more than {MaximumTagsPerApp} tags.");
            }
        }

        return normalized;
    }

    public static string? Serialize(IEnumerable<string>? tags)
    {
        var normalized = Normalize(tags);
        return normalized.Count == 0 ? null : string.Join(',', normalized);
    }

    public static string? Serialize(string? tags) => Serialize(Normalize(tags));
}
