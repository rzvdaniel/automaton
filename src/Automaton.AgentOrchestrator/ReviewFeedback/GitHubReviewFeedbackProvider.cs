using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;

namespace Automaton.AgentOrchestrator.ReviewFeedback;

public sealed record GitHubReviewFeedbackProviderOptions(string Owner, string Repository, string Token, Uri ApiBaseUri, string AutomationLogin);

public sealed class ReviewFeedbackProviderException(string message, bool isRetryable = false) : Exception(message)
{
    public bool IsRetryable { get; } = isRetryable;
}

public sealed class GitHubReviewFeedbackProvider(HttpClient client, GitHubReviewFeedbackProviderOptions options) : IReviewFeedbackProvider
{
    private const int MaximumResponseBytes = 256 * 1024;
    private const int MaximumPages = 100;
    private const int MaximumItems = 50;
    private static readonly Regex NextLink = new("<(?<url>[^>]+)>;\\s*rel=\"next\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<ReviewFeedbackSnapshot> GetAsync(ReviewFeedbackTarget target, CancellationToken cancellationToken)
    {
        ValidateTarget(target);
        var pull = await GetJsonAsync<PullResponse>(new(options.ApiBaseUri, $"repos/{Encode(options.Owner)}/{Encode(options.Repository)}/pulls/{target.PullRequestNumber}"), cancellationToken)
            ?? throw new ReviewFeedbackProviderException("GitHub returned an empty pull-request response.");
        if (!string.Equals(pull.State, "open", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(pull.Head?.Ref, target.HeadBranch, StringComparison.Ordinal)
            || !string.Equals(pull.Head?.Sha, target.HeadSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(pull.Base?.Repository?.FullName, target.RepositoryIdentity, StringComparison.OrdinalIgnoreCase))
            throw new ReviewFeedbackProviderException("GitHub pull-request identity does not match the exact feedback target.");

        var reviews = await GetPagesAsync<ReviewResponse>($"repos/{Encode(options.Owner)}/{Encode(options.Repository)}/pulls/{target.PullRequestNumber}/reviews?per_page=100", cancellationToken);
        var comments = await GetPagesAsync<ReviewCommentResponse>($"repos/{Encode(options.Owner)}/{Encode(options.Repository)}/pulls/{target.PullRequestNumber}/comments?per_page=100", cancellationToken);
        var currentReviews = new Dictionary<string, ReviewResponse>(StringComparer.OrdinalIgnoreCase);
        foreach (var review in reviews.Where(review => IsHuman(review.User) && string.Equals(review.CommitId, target.HeadSha, StringComparison.OrdinalIgnoreCase)))
            currentReviews[review.User!.Login!] = review;

        var items = new List<ReviewFeedbackItem>();
        foreach (var review in currentReviews.Values.Where(review => string.Equals(review.State, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase)))
            items.Add(new($"review:{review.Id}", ReviewFeedbackKinds.ChangesRequested, Bound(review.User?.Login, 100) ?? "unknown", Bound(review.Body, 2000) ?? "Changes requested.", CommitSha: target.HeadSha, Url: Bound(review.HtmlUrl, 2048)));
        foreach (var comment in comments.Where(comment => IsHuman(comment.User) && string.Equals(comment.CommitId, target.HeadSha, StringComparison.OrdinalIgnoreCase)))
            items.Add(new($"comment:{comment.Id}", ReviewFeedbackKinds.ReviewComment, Bound(comment.User?.Login, 100) ?? "unknown", Bound(comment.Body, 2000) ?? "Review comment.", Bound(comment.Path, 500), comment.Line ?? comment.OriginalLine, target.HeadSha, Bound(comment.HtmlUrl, 2048)));
        if (items.Count > MaximumItems) throw new ReviewFeedbackProviderException("GitHub returned too many actionable review feedback items.");

        var status = items.Count > 0 ? ReviewFeedbackStatuses.Actionable
            : currentReviews.Values.Any(review => string.Equals(review.State, "APPROVED", StringComparison.OrdinalIgnoreCase)) ? ReviewFeedbackStatuses.Approved
            : ReviewFeedbackStatuses.AwaitingHuman;
        return new(target.HeadSha.ToLowerInvariant(), status, items);
    }

    private async Task<IReadOnlyList<T>> GetPagesAsync<T>(string relative, CancellationToken cancellationToken)
    {
        var values = new List<T>();
        Uri? next = new(options.ApiBaseUri, relative);
        for (var page = 0; next is not null; page++)
        {
            if (page >= MaximumPages) throw new ReviewFeedbackProviderException("GitHub feedback pagination exceeded the allowed page count.");
            var result = await GetResponseAsync(next, cancellationToken);
            using (result)
            {
                var body = await DeserializeAsync<T[]>(result, cancellationToken) ?? throw new ReviewFeedbackProviderException("GitHub feedback response was malformed.");
                values.AddRange(body);
                next = ParseNext(result);
            }
        }
        return values;
    }

    private async Task<T?> GetJsonAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await GetResponseAsync(uri, cancellationToken);
        return await DeserializeAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> GetResponseAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!SameOrigin(uri)) throw new ReviewFeedbackProviderException("GitHub feedback URL is outside the configured API origin.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("Automaton-Agent-Orchestrator");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (HttpRequestException) { throw new ReviewFeedbackProviderException("GitHub feedback request failed temporarily.", true); }
        if (!response.IsSuccessStatusCode)
        {
            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            response.Dispose();
            throw new ReviewFeedbackProviderException($"GitHub feedback request failed with HTTP {(int)response.StatusCode}.", retryable);
        }
        return response;
    }

    private Uri? ParseNext(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values)) return null;
        var match = NextLink.Match(string.Join(',', values));
        if (!match.Success) return null;
        if (!Uri.TryCreate(options.ApiBaseUri, match.Groups["url"].Value, out var next) || !SameOrigin(next))
            throw new ReviewFeedbackProviderException("GitHub feedback pagination link is outside the configured API origin.");
        return next;
    }

    private bool IsHuman(UserResponse? user) => user is { Login: not null }
        && !string.Equals(user.Type, "Bot", StringComparison.OrdinalIgnoreCase)
        && !user.Login.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(user.Login, options.AutomationLogin, StringComparison.OrdinalIgnoreCase);
    private void ValidateTarget(ReviewFeedbackTarget target)
    {
        if (!string.Equals(target.RepositoryIdentity, $"{options.Owner}/{options.Repository}", StringComparison.OrdinalIgnoreCase)
            || target.PullRequestNumber <= 0 || target.HeadSha.Length != 40 || !target.HeadSha.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(target.HeadBranch))
            throw new ReviewFeedbackProviderException("Review feedback target identity is invalid.");
    }
    private bool SameOrigin(Uri uri) => string.Equals(uri.Scheme, options.ApiBaseUri.Scheme, StringComparison.OrdinalIgnoreCase) && string.Equals(uri.Host, options.ApiBaseUri.Host, StringComparison.OrdinalIgnoreCase) && uri.Port == options.ApiBaseUri.Port;
    private static string Encode(string value) => Uri.EscapeDataString(value);
    private static string? Bound(string? value, int maximum) => string.IsNullOrWhiteSpace(value) ? null : new string(value.Where(character => !char.IsControl(character) || character is '\n' or '\t').Take(maximum).ToArray()).Trim();
    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new ReviewFeedbackProviderException("GitHub feedback response exceeded the allowed size.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var bounded = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (bounded.Length + read > MaximumResponseBytes) throw new ReviewFeedbackProviderException("GitHub feedback response exceeded the allowed size.");
            await bounded.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        bounded.Position = 0;
        try { return await JsonSerializer.DeserializeAsync<T>(bounded, JsonOptions, cancellationToken); }
        catch (JsonException) { throw new ReviewFeedbackProviderException("GitHub feedback response was malformed."); }
    }

    private sealed class PullResponse { public string? State { get; init; } public GitRef? Head { get; init; } public GitRef? Base { get; init; } }
    private sealed class GitRef { public string? Ref { get; init; } public string? Sha { get; init; } [JsonPropertyName("repo")] public Repository? Repository { get; init; } }
    private sealed class Repository { [JsonPropertyName("full_name")] public string? FullName { get; init; } }
    private sealed class UserResponse { public string? Login { get; init; } public string? Type { get; init; } }
    private sealed class ReviewResponse { public long Id { get; init; } public string? State { get; init; } public string? Body { get; init; } [JsonPropertyName("commit_id")] public string? CommitId { get; init; } [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; } public UserResponse? User { get; init; } }
    private sealed class ReviewCommentResponse { public long Id { get; init; } public string? Body { get; init; } public string? Path { get; init; } public int? Line { get; init; } [JsonPropertyName("original_line")] public int? OriginalLine { get; init; } [JsonPropertyName("commit_id")] public string? CommitId { get; init; } [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; } public UserResponse? User { get; init; } }
}
