using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;

namespace Automaton.Api.Services;

public sealed class AdminDocumentationService
{
    private static readonly HashSet<string> AllowedAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".svg"
    };

    private static readonly FileExtensionContentTypeProvider AssetContentTypes = new();
    private static readonly Regex FrontMatterRegex = new(
        @"\A---\r?\n(?<frontMatter>[\s\S]*?)\r?\n---\r?\n?",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private readonly IWebHostEnvironment _environment;

    public AdminDocumentationService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public IReadOnlyList<AdminDocumentationTopicListItemResponse> ListTopics()
    {
        return DiscoverTopics()
            .Select(topic => new AdminDocumentationTopicListItemResponse
            {
                Key = topic.Key,
                Title = topic.Title,
                Category = topic.Category,
                Group = topic.Group,
                SortOrder = topic.SortOrder
            })
            .ToList();
    }

    public async Task<AdminDocumentationTopicResponse?> GetTopicAsync(string key, CancellationToken cancellationToken)
    {
        var topic = DiscoverTopics()
            .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));

        if (topic is null || !File.Exists(topic.AbsolutePath))
        {
            return null;
        }

        var contentMarkdown = await File.ReadAllTextAsync(topic.AbsolutePath, cancellationToken);
        var parsed = ParseFrontMatter(contentMarkdown);
        var updatedAtUtc = File.GetLastWriteTimeUtc(topic.AbsolutePath);

        return new AdminDocumentationTopicResponse
        {
            Key = topic.Key,
            Title = topic.Title,
            Category = topic.Category,
            Group = topic.Group,
            SortOrder = topic.SortOrder,
            ContentMarkdown = parsed.Body,
            UpdatedAtUtc = updatedAtUtc
        };
    }

    public DocumentationAsset? GetAsset(string group, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(relativePath)
            || group is "." or ".."
            || group.Contains('/') || group.Contains('\\')
            || relativePath.Contains('\0')
            || relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(segment => segment == "..")
            || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var adminRoot = Path.Combine(_environment.ContentRootPath, "Documentation", "Admin");
        var assetsRoot = Path.GetFullPath(Path.Combine(adminRoot, group, "assets"));
        var candidatePath = Path.GetFullPath(Path.Combine(assetsRoot, relativePath));
        var assetsRootWithSeparator = assetsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!candidatePath.StartsWith(assetsRootWithSeparator, StringComparison.OrdinalIgnoreCase)
            || !AllowedAssetExtensions.Contains(Path.GetExtension(candidatePath))
            || !File.Exists(candidatePath))
        {
            return null;
        }

        if (!AssetContentTypes.TryGetContentType(candidatePath, out var contentType))
        {
            return null;
        }

        return new DocumentationAsset(candidatePath, contentType);
    }

    private List<DocumentationTopicDefinition> DiscoverTopics()
    {
        var adminRoot = Path.Combine(_environment.ContentRootPath, "Documentation", "Admin");
        if (!Directory.Exists(adminRoot))
        {
            return [];
        }

        var topics = new List<DocumentationTopicDefinition>();
        foreach (var bookDirectory in Directory.GetDirectories(adminRoot))
        {
            var group = Path.GetFileName(bookDirectory);
            if (string.IsNullOrWhiteSpace(group))
            {
                continue;
            }

            var indexPath = Path.Combine(bookDirectory, "index.md");
            var bookMeta = ReadBookMetadata(indexPath, group);

            var pageFiles = Directory
                .GetFiles(bookDirectory, "*.md", SearchOption.TopDirectoryOnly)
                .Where(path => !string.Equals(Path.GetFileName(path), "index.md", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var pageFile in pageFiles)
            {
                var pageSlug = Path.GetFileNameWithoutExtension(pageFile);
                if (string.IsNullOrWhiteSpace(pageSlug))
                {
                    continue;
                }

                var parsed = ParseFrontMatter(File.ReadAllText(pageFile));
                var pageTitle = parsed.Metadata.GetValueOrDefault("title")
                    ?? HumanizeSlug(pageSlug);
                var pageOrder = ParseOrder(parsed.Metadata.GetValueOrDefault("order"));

                topics.Add(new DocumentationTopicDefinition(
                    Key: $"admin.{group}.{pageSlug}",
                    Title: pageTitle,
                    Category: bookMeta.Title,
                    Group: group,
                    SortOrder: pageOrder,
                    AbsolutePath: pageFile));
            }
        }

        return topics
            .OrderBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SortOrder)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static BookMetadata ReadBookMetadata(string indexPath, string fallbackGroup)
    {
        if (!File.Exists(indexPath))
        {
            return new BookMetadata(HumanizeSlug(fallbackGroup), 0);
        }

        var parsed = ParseFrontMatter(File.ReadAllText(indexPath));
        return new BookMetadata(
            parsed.Metadata.GetValueOrDefault("title") ?? HumanizeSlug(fallbackGroup),
            ParseOrder(parsed.Metadata.GetValueOrDefault("order")));
    }

    private static ParsedDocumentationFrontMatter ParseFrontMatter(string content)
    {
        var source = content ?? string.Empty;
        var match = FrontMatterRegex.Match(source);
        if (!match.Success)
        {
            return new ParsedDocumentationFrontMatter(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), source);
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in match.Groups["frontMatter"].Value.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('"', '\'');
            metadata[key] = value;
        }

        return new ParsedDocumentationFrontMatter(metadata, source[match.Length..]);
    }

    private static int ParseOrder(string? rawOrder)
    {
        return int.TryParse(rawOrder, out var order) ? order : 0;
    }

    private static string HumanizeSlug(string value)
    {
        return string.Join(
            ' ',
            value
                .Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private sealed record DocumentationTopicDefinition(
        string Key,
        string Title,
        string Category,
        string Group,
        int SortOrder,
        string AbsolutePath);

    private sealed record BookMetadata(string Title, int SortOrder);

    private sealed record ParsedDocumentationFrontMatter(
        IReadOnlyDictionary<string, string> Metadata,
        string Body);
}

public sealed record DocumentationAsset(string AbsolutePath, string ContentType);

public sealed class AdminDocumentationTopicListItemResponse
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class AdminDocumentationTopicResponse
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string ContentMarkdown { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
