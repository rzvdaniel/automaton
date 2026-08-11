using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;

namespace Automaton.AgentOrchestrator.Trackers.GitHub;

/// <summary>GitHub REST implementation. Tokens are used only in outbound host requests and are never logged.</summary>
public sealed class GitHubIssuesTracker(HttpClient client, GitHubIssuesTrackerOptions options) : IWorkTracker
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex NextLink = new("<(?<url>[^>]+)>;\\s*rel=\"next\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<IReadOnlyList<WorkItem>> DiscoverCandidatesAsync(CancellationToken cancellationToken)
    {
        var result = new List<WorkItem>();
        Uri? next = new Uri(options.ApiBaseUri, $"repos/{Encode(options.Owner)}/{Encode(options.Repository)}/issues?state=open&per_page=100");
        while (next is not null)
        {
            using var response = await SendAsync(next, cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return [];
            EnsureSuccess(response);
            var issues = await DeserializeAsync<GitHubIssue[]>(response, cancellationToken) ?? [];
            result.AddRange(issues.Where(IsEligibleIssue).Select(Normalize));
            next = ParseNextLink(response, options.ApiBaseUri);
        }
        return result;
    }

    public async Task<WorkItem?> GetByIdAsync(string workItemId, CancellationToken cancellationToken)
    {
        if (!TryParseWorkItemId(workItemId, out var number)) return null;
        var uri = new Uri(options.ApiBaseUri, $"repos/{Encode(options.Owner)}/{Encode(options.Repository)}/issues/{number}");
        using var response = await SendAsync(uri, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return null;
        EnsureSuccess(response);
        var issue = await DeserializeAsync<GitHubIssue>(response, cancellationToken);
        return issue is null || issue.PullRequest is not null ? null : Normalize(issue);
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("Automaton-Agent-Orchestrator");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        return await client.SendAsync(request, cancellationToken);
    }

    private bool IsEligibleIssue(GitHubIssue issue)
        => issue.PullRequest is null
           && string.Equals(issue.State, "open", StringComparison.OrdinalIgnoreCase)
           && options.RequiredLabels.All(required => NormalizeLabels(issue.Labels).Contains(required.Trim().ToLowerInvariant(), StringComparer.Ordinal));

    private WorkItem Normalize(GitHubIssue issue)
        => new(CreateWorkItemId(issue.Number), $"{options.Owner}/{options.Repository}#{issue.Number}", issue.Title ?? string.Empty,
            issue.Body, (issue.State ?? string.Empty).Trim().ToLowerInvariant(), NormalizeLabels(issue.Labels),
            Uri.TryCreate(issue.HtmlUrl, UriKind.Absolute, out var url) ? url : null, issue.CreatedAt, issue.UpdatedAt, issue.Assignee?.Login);

    private static IReadOnlyList<string> NormalizeLabels(IEnumerable<GitHubLabel>? labels)
        => (labels ?? []).Select(x => x.Name?.Trim().ToLowerInvariant()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>()
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static string Encode(string value) => Uri.EscapeDataString(value);
    private string CreateWorkItemId(long number) => $"github-issue:{options.Owner}/{options.Repository}:{number}";
    private bool TryParseWorkItemId(string value, out long number)
    {
        number = 0;
        var prefix = $"github-issue:{options.Owner}/{options.Repository}:";
        return value.StartsWith(prefix, StringComparison.Ordinal) && long.TryParse(value[prefix.Length..], out number) && number > 0;
    }

    private static Uri? ParseNextLink(HttpResponseMessage response, Uri baseUri)
    {
        if (!response.Headers.TryGetValues("Link", out var links)) return null;
        var match = NextLink.Match(string.Join(",", links));
        return match.Success && Uri.TryCreate(baseUri, match.Groups["url"].Value, out var next) ? next : null;
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        => await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(cancellationToken), JsonOptions, cancellationToken);

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw new GitHubTrackerException(response.StatusCode);
    }

    private sealed class GitHubIssue
    {
        public long Number { get; init; }
        public string? Title { get; init; }
        public string? Body { get; init; }
        public string? State { get; init; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
        [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
        public GitHubLabel[]? Labels { get; init; }
        [JsonPropertyName("pull_request")] public JsonElement? PullRequest { get; init; }
        public GitHubUser? Assignee { get; init; }
    }

    private sealed class GitHubLabel { public string? Name { get; init; } }
    private sealed class GitHubUser { public string? Login { get; init; } }
}

public sealed class GitHubTrackerException(HttpStatusCode statusCode) : Exception($"GitHub Issues request failed with HTTP {(int)statusCode}.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
