namespace Automaton.Api.Services;

public sealed class AdminDocumentationService
{
    private readonly IWebHostEnvironment _environment;

    private static readonly IReadOnlyDictionary<string, DocumentationTopicDefinition> Topics =
        new Dictionary<string, DocumentationTopicDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["ai-guidance"] = new(
                "ai-guidance",
                "AI Guidance Handbook",
                Path.Combine("Documentation", "Admin", "ai-guidance.md"))
        };

    public AdminDocumentationService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<AdminDocumentationTopicResponse?> GetTopicAsync(string key, CancellationToken cancellationToken)
    {
        if (!Topics.TryGetValue(key, out var topic))
        {
            return null;
        }

        var absolutePath = Path.Combine(_environment.ContentRootPath, topic.RelativePath);
        if (!File.Exists(absolutePath))
        {
            return null;
        }

        var contentMarkdown = await File.ReadAllTextAsync(absolutePath, cancellationToken);
        var updatedAtUtc = File.GetLastWriteTimeUtc(absolutePath);

        return new AdminDocumentationTopicResponse
        {
            Key = topic.Key,
            Title = topic.Title,
            ContentMarkdown = contentMarkdown,
            UpdatedAtUtc = updatedAtUtc
        };
    }

    private sealed record DocumentationTopicDefinition(string Key, string Title, string RelativePath);
}

public sealed class AdminDocumentationTopicResponse
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ContentMarkdown { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
