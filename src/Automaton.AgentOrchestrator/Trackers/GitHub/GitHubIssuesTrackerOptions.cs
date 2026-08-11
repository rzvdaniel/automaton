using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.Trackers.GitHub;

public sealed record GitHubIssuesTrackerOptions(string Owner, string Repository, string Token, IReadOnlyList<string> RequiredLabels, Uri ApiBaseUri)
{
    public const string TrackerKind = "github-issues";

    public static GitHubIssuesTrackerOptions From(TrackerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.Equals(options.Kind, TrackerKind, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"tracker.kind must be '{TrackerKind}' for the GitHub Issues adapter.");

        var requiredLabels = options.RequiredLabels
            .Select(label => label.Trim().ToLowerInvariant())
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requiredLabels.Length == 0)
            throw new InvalidOperationException("tracker.required_labels must contain at least one opt-in label for GitHub Issues.");

        return new GitHubIssuesTrackerOptions(
            Required(options.Provider, "owner"),
            Required(options.Provider, "repository"),
            Required(options.Provider, "token"),
            requiredLabels,
            options.Provider.TryGetValue("api_base_url", out var baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
                ? uri : new Uri("https://api.github.com/"));
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new InvalidOperationException($"tracker.provider.{key} is required for GitHub Issues.");
}
