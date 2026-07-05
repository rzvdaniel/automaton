using System.Text.RegularExpressions;

namespace Automaton.Api.Services;

internal static class AiGuidanceMarkdownFrontMatter
{
    private static readonly Regex FrontMatterRegex = new(
        @"\A---\r?\n(?<frontMatter>[\s\S]*?)\r?\n---\r?\n?",
        RegexOptions.Compiled);

    public static ParsedGuidanceMarkdown Parse(string? markdown)
    {
        var content = markdown ?? string.Empty;
        var match = FrontMatterRegex.Match(content);
        if (!match.Success)
        {
            return new ParsedGuidanceMarkdown(null, null, content);
        }

        var title = default(string);
        var description = default(string);
        foreach (var line in match.Groups["frontMatter"].Value.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (TryReadScalar(line, "title", out var parsedTitle))
            {
                title = parsedTitle;
                continue;
            }

            if (TryReadScalar(line, "description", out var parsedDescription))
            {
                description = parsedDescription;
            }
        }

        var body = content[match.Length..];
        return new ParsedGuidanceMarkdown(title, description, body);
    }

    private static bool TryReadScalar(string line, string key, out string? value)
    {
        var prefix = key + ":";
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return false;
        }

        var raw = line[prefix.Length..].Trim();
        value = UnescapeScalar(raw);
        return true;
    }

    private static string UnescapeScalar(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            trimmed = trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        }

        return trimmed;
    }
}

internal sealed record ParsedGuidanceMarkdown(
    string? Title,
    string? Description,
    string Body);
