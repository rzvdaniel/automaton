using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;

namespace Automaton.AgentOrchestrator.Publication;

public sealed record GitHubPullRequestPublisherOptions(string Owner, string Repository, string Token, Uri ApiBaseUri);

public sealed class GitHubPullRequestPublisher(HttpClient client, GitHubPullRequestPublisherOptions options) : IPullRequestPublisher
{
    private const int MaximumResponseBytes = 256 * 1024;
    private static readonly Regex NextLink = new("<(?<url>[^>]+)>;\\s*rel=\"next\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<PullRequestPublication> FindOrCreateAsync(PullRequestPublicationRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Repository, $"{options.Owner}/{options.Repository}", StringComparison.OrdinalIgnoreCase))
            throw new PublicationException("Pull-request repository does not match configured GitHub publication scope.");
        var matches = await FindAsync(request, "open", cancellationToken);
        if (matches.Count > 1) throw new PublicationException("Multiple matching open pull requests exist for the publication branch.");
        if (matches.Count == 1) return NormalizeAndValidate(matches[0], request);
        if ((await FindAsync(request, "closed", cancellationToken)).Count > 0)
            throw new PublicationException("A closed pull request already exists for the publication branch; manual attention is required.");

        var uri = new Uri(options.ApiBaseUri, $"repos/{Encode(options.Owner)}/{Encode(options.Repository)}/pulls");
        var payload = JsonSerializer.Serialize(new { title = request.Title, head = request.HeadBranch, @base = request.BaseBranch, body = request.Body, draft = true });
        using var response = await SendAsync(HttpMethod.Post, uri, payload, cancellationToken);
        EnsureSuccess(response);
        var created = await DeserializeAsync<GitHubPullRequest>(response, cancellationToken)
            ?? throw new PublicationException("GitHub returned an empty pull-request response.");
        return NormalizeAndValidate(created, request);
    }

    private async Task<List<GitHubPullRequest>> FindAsync(PullRequestPublicationRequest request, string state, CancellationToken cancellationToken)
    {
        var result = new List<GitHubPullRequest>();
        Uri? next = new(options.ApiBaseUri,
            $"repos/{Encode(options.Owner)}/{Encode(options.Repository)}/pulls?state={state}&head={Encode(options.Owner + ":" + request.HeadBranch)}&base={Encode(request.BaseBranch)}&per_page=100");
        while (next is not null)
        {
            using var response = await SendAsync(HttpMethod.Get, next, null, cancellationToken);
            EnsureSuccess(response);
            result.AddRange(await DeserializeAsync<GitHubPullRequest[]>(response, cancellationToken) ?? []);
            next = ParseNext(response);
        }
        return result.Where(item => string.Equals(item.State, state, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(item.Head?.Ref, request.HeadBranch, StringComparison.Ordinal)
                                    && string.Equals(item.Base?.Ref, request.BaseBranch, StringComparison.Ordinal)).ToList();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, string? json, CancellationToken cancellationToken)
    {
        if (!SameOrigin(uri)) throw new PublicationException("GitHub pull-request URL is outside the configured API origin.");
        using var message = new HttpRequestMessage(method, uri);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        message.Headers.UserAgent.ParseAdd("Automaton-Agent-Orchestrator");
        message.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        if (json is not null) message.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private Uri? ParseNext(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values)) return null;
        var match = NextLink.Match(string.Join(',', values));
        if (!match.Success) return null;
        if (!Uri.TryCreate(options.ApiBaseUri, match.Groups["url"].Value, out var next) || !SameOrigin(next))
            throw new PublicationException("GitHub pull-request pagination link is outside the configured API origin.");
        return next;
    }

    private bool SameOrigin(Uri uri)
        => string.Equals(uri.Scheme, options.ApiBaseUri.Scheme, StringComparison.OrdinalIgnoreCase)
           && string.Equals(uri.Host, options.ApiBaseUri.Host, StringComparison.OrdinalIgnoreCase)
           && uri.Port == options.ApiBaseUri.Port;

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
        throw new PublicationException($"GitHub pull-request request failed with HTTP {(int)response.StatusCode}.", retryable);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new PublicationException("GitHub pull-request response exceeded the allowed size.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var bounded = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (bounded.Length + read > MaximumResponseBytes) throw new PublicationException("GitHub pull-request response exceeded the allowed size.");
            await bounded.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        bounded.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(bounded, JsonOptions, cancellationToken);
    }

    private static PullRequestPublication NormalizeAndValidate(GitHubPullRequest item, PullRequestPublicationRequest expected)
    {
        var repository = item.Base?.Repository?.FullName ?? string.Empty;
        var result = new PullRequestPublication(item.Number, item.HtmlUrl ?? string.Empty, repository,
            item.Head?.Ref ?? string.Empty, item.Head?.Sha ?? string.Empty, item.Base?.Ref ?? string.Empty,
            item.State ?? string.Empty, item.Draft);
        if (!string.Equals(result.Repository, expected.Repository, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(result.HeadBranch, expected.HeadBranch, StringComparison.Ordinal)
            || !string.Equals(result.HeadSha, expected.HeadSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(result.BaseBranch, expected.BaseBranch, StringComparison.Ordinal)
            || !string.Equals(result.State, "open", StringComparison.OrdinalIgnoreCase)
            || !result.Draft
            || result.Number <= 0
            || result.Url.Length > 2048
            || !Uri.TryCreate(result.Url, UriKind.Absolute, out _))
            throw new PublicationException("GitHub returned an unexpected pull-request identity.");
        return result;
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);

    private sealed class GitHubPullRequest
    {
        public int Number { get; init; }
        public string? State { get; init; }
        public bool Draft { get; init; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; }
        public GitRef? Head { get; init; }
        public GitRef? Base { get; init; }
    }
    private sealed class GitRef
    {
        public string? Ref { get; init; }
        public string? Sha { get; init; }
        [JsonPropertyName("repo")] public GitRepository? Repository { get; init; }
    }
    private sealed class GitRepository { [JsonPropertyName("full_name")] public string? FullName { get; init; } }
}
