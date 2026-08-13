using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Publication;

namespace Automaton.AgentOrchestrator.Ci;

public sealed record GitHubCiProviderOptions(string Owner, string Repository, string Token, Uri ApiBaseUri, IReadOnlyList<string> RequiredChecks);

public sealed class CiProviderException(string message, bool isRetryable = false) : Exception(message)
{
    public bool IsRetryable { get; } = isRetryable;
}

public sealed class GitHubCiProvider(HttpClient client, GitHubCiProviderOptions options) : ICiProvider
{
    private const int MaximumResponseBytes = 256 * 1024;
    private const int MaximumPages = 100;
    private static readonly Regex NextLink = new("<(?<url>[^>]+)>;\\s*rel=\"next\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<CiSnapshot> GetAsync(CiTarget target, CancellationToken cancellationToken)
    {
        if (!string.Equals(target.RepositoryIdentity, $"{options.Owner}/{options.Repository}", StringComparison.OrdinalIgnoreCase)
            || target.PullRequestNumber <= 0 || !IsSha(target.HeadSha) || string.IsNullOrWhiteSpace(target.HeadBranch))
            throw new CiProviderException("CI target identity is invalid.");

        var pull = await GetJsonAsync<PullResponse>(new(options.ApiBaseUri, $"repos/{Encode(options.Owner)}/{Encode(options.Repository)}/pulls/{target.PullRequestNumber}"), cancellationToken)
            ?? throw new CiProviderException("GitHub returned an empty pull-request identity response.");
        if (!string.Equals(pull.State, "open", StringComparison.OrdinalIgnoreCase) || pull.Draft != true
            || !string.Equals(pull.Head?.Ref, target.HeadBranch, StringComparison.Ordinal)
            || !string.Equals(pull.Head?.Sha, target.HeadSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(pull.Base?.Repository?.FullName, target.RepositoryIdentity, StringComparison.OrdinalIgnoreCase))
            throw new CiProviderException("GitHub pull-request head does not match the exact CI target.", true);

        var checkRuns = await GetCheckRunsAsync(target.HeadSha, cancellationToken);
        var commitStatuses = await GetCommitStatusesAsync(target.HeadSha, cancellationToken);
        var normalized = checkRuns.Where(check => string.Equals(check.HeadSha, target.HeadSha, StringComparison.OrdinalIgnoreCase))
            .Select(Normalize)
            .Concat(commitStatuses.Where(status => string.Equals(status.Sha, target.HeadSha, StringComparison.OrdinalIgnoreCase)).Select(Normalize))
            .ToArray();
        var selected = SelectRequired(normalized);
        var status = Aggregate(selected);
        return new(target.HeadSha.ToLowerInvariant(), status, selected);
    }

    private async Task<IReadOnlyList<CheckRun>> GetCheckRunsAsync(string headSha, CancellationToken cancellationToken)
    {
        var checks = new List<CheckRun>();
        Uri? next = new(options.ApiBaseUri, $"repos/{Encode(options.Owner)}/{Encode(options.Repository)}/commits/{headSha}/check-runs?per_page=100");
        for (var pageNumber = 0; next is not null; pageNumber++)
        {
            if (pageNumber >= MaximumPages) throw new CiProviderException("GitHub CI pagination exceeded the allowed page count.");
            var page = await GetResponseAsync(next, cancellationToken);
            using (page.Response)
            {
                var body = await DeserializeAsync<ChecksResponse>(page.Response, cancellationToken)
                    ?? throw new CiProviderException("GitHub check-runs response was malformed.");
                checks.AddRange(body.CheckRuns ?? throw new CiProviderException("GitHub check-runs response was malformed."));
                next = ParseNext(page.Response);
            }
        }
        return checks;
    }

    private async Task<IReadOnlyList<CommitStatus>> GetCommitStatusesAsync(string headSha, CancellationToken cancellationToken)
    {
        var statuses = new List<CommitStatus>();
        var observedContexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Uri? next = new(options.ApiBaseUri, $"repos/{Encode(options.Owner)}/{Encode(options.Repository)}/commits/{headSha}/statuses?per_page=100");
        for (var pageNumber = 0; next is not null; pageNumber++)
        {
            if (pageNumber >= MaximumPages) throw new CiProviderException("GitHub CI pagination exceeded the allowed page count.");
            var page = await GetResponseAsync(next, cancellationToken);
            using (page.Response)
            {
                var body = await DeserializeAsync<CommitStatus[]>(page.Response, cancellationToken)
                    ?? throw new CiProviderException("GitHub commit-status response was malformed.");
                foreach (var status in body)
                {
                    if (string.Equals(status.Sha, headSha, StringComparison.OrdinalIgnoreCase)
                        && observedContexts.Add(status.Context ?? string.Empty))
                        statuses.Add(status);
                }
                next = ParseNext(page.Response);
            }
        }
        return statuses;
    }

    private IReadOnlyList<CiCheck> SelectRequired(IReadOnlyList<CiCheck> checks)
    {
        if (options.RequiredChecks.Count == 0) return checks.Count == 0 ? [new("CI checks", CiStatuses.Pending, FailureSummary: "No checks discovered for the exact commit.")] : checks;
        var selected = new List<CiCheck>();
        foreach (var name in options.RequiredChecks.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matches = checks.Where(check => string.Equals(check.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            selected.AddRange(matches.Length == 0 ? [new CiCheck(name, CiStatuses.Pending, FailureSummary: "Required check has not appeared for the exact commit.")] : matches);
        }
        return selected;
    }

    private static string Aggregate(IReadOnlyList<CiCheck> checks)
    {
        if (checks.Any(check => check.Status == CiStatuses.Failed)) return CiStatuses.Failed;
        if (checks.Any(check => check.Status == CiStatuses.Pending)) return CiStatuses.Pending;
        if (checks.Any(check => check.Status == CiStatuses.Indeterminate)) return CiStatuses.Indeterminate;
        return checks.Count > 0 && checks.All(check => check.Status == CiStatuses.Passed) ? CiStatuses.Passed : CiStatuses.Indeterminate;
    }

    private static CiCheck Normalize(CheckRun check)
    {
        var conclusion = check.Conclusion?.ToLowerInvariant();
        var status = !string.Equals(check.Status, "completed", StringComparison.OrdinalIgnoreCase)
            ? CiStatuses.Pending
            : conclusion is "success" ? CiStatuses.Passed
            : conclusion is "failure" or "cancelled" or "timed_out" or "action_required" or "startup_failure" ? CiStatuses.Failed
            : CiStatuses.Indeterminate;
        var summary = Bound(check.Output?.Summary ?? check.Output?.Title, 2000);
        return new(Bound(check.Name, 200) ?? "unnamed-check", status, conclusion, Bound(check.DetailsUrl, 2048), summary, check.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static CiCheck Normalize(CommitStatus commitStatus)
    {
        var providerStatus = commitStatus.State?.ToLowerInvariant();
        var status = providerStatus is "success" ? CiStatuses.Passed
            : providerStatus is "pending" ? CiStatuses.Pending
            : providerStatus is "failure" or "error" ? CiStatuses.Failed
            : CiStatuses.Indeterminate;
        return new(
            Bound(commitStatus.Context, 200) ?? "unnamed-status",
            status,
            providerStatus,
            Bound(commitStatus.TargetUrl, 2048),
            Bound(commitStatus.Description, 2000),
            commitStatus.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task<T?> GetJsonAsync<T>(Uri uri, CancellationToken cancellationToken)
    {
        var result = await GetResponseAsync(uri, cancellationToken);
        using (result.Response) return await DeserializeAsync<T>(result.Response, cancellationToken);
    }

    private async Task<(HttpResponseMessage Response, Uri Uri)> GetResponseAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!SameOrigin(uri)) throw new CiProviderException("GitHub CI URL is outside the configured API origin.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("Automaton-Agent-Orchestrator");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (HttpRequestException) { throw new CiProviderException("GitHub CI request failed temporarily.", true); }
        if (!response.IsSuccessStatusCode)
        {
            var retryable = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            response.Dispose();
            throw new CiProviderException($"GitHub CI request failed with HTTP {(int)response.StatusCode}.", retryable);
        }
        return (response, uri);
    }

    private Uri? ParseNext(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Link", out var values)) return null;
        var match = NextLink.Match(string.Join(',', values));
        if (!match.Success) return null;
        if (!Uri.TryCreate(options.ApiBaseUri, match.Groups["url"].Value, out var next) || !SameOrigin(next))
            throw new CiProviderException("GitHub CI pagination link is outside the configured API origin.");
        return next;
    }

    private bool SameOrigin(Uri uri) => string.Equals(uri.Scheme, options.ApiBaseUri.Scheme, StringComparison.OrdinalIgnoreCase) && string.Equals(uri.Host, options.ApiBaseUri.Host, StringComparison.OrdinalIgnoreCase) && uri.Port == options.ApiBaseUri.Port;
    private static string Encode(string value) => Uri.EscapeDataString(value);
    private static bool IsSha(string value) => value.Length == 40 && value.All(Uri.IsHexDigit);
    private static string? Bound(string? value, int maximum) => string.IsNullOrWhiteSpace(value) ? null : new string(value.Where(character => !char.IsControl(character) || character is '\n' or '\t').Take(maximum).ToArray()).Trim();

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new CiProviderException("GitHub CI response exceeded the allowed size.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var bounded = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (bounded.Length + read > MaximumResponseBytes) throw new CiProviderException("GitHub CI response exceeded the allowed size.");
            await bounded.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        bounded.Position = 0;
        try { return await JsonSerializer.DeserializeAsync<T>(bounded, JsonOptions, cancellationToken); }
        catch (JsonException) { throw new CiProviderException("GitHub CI response was malformed."); }
    }

    private sealed class PullResponse { public string? State { get; init; } public bool? Draft { get; init; } public GitRef? Head { get; init; } public GitRef? Base { get; init; } }
    private sealed class GitRef { public string? Ref { get; init; } public string? Sha { get; init; } [JsonPropertyName("repo")] public Repository? Repository { get; init; } }
    private sealed class Repository { [JsonPropertyName("full_name")] public string? FullName { get; init; } }
    private sealed class ChecksResponse { [JsonPropertyName("check_runs")] public CheckRun[]? CheckRuns { get; init; } }
    private sealed class CheckRun { public long Id { get; init; } public string? Name { get; init; } public string? Status { get; init; } public string? Conclusion { get; init; } [JsonPropertyName("head_sha")] public string? HeadSha { get; init; } [JsonPropertyName("details_url")] public string? DetailsUrl { get; init; } public CheckOutput? Output { get; init; } }
    private sealed class CheckOutput { public string? Title { get; init; } public string? Summary { get; init; } }
    private sealed class CommitStatus { public long Id { get; init; } public string? State { get; init; } public string? Context { get; init; } public string? Description { get; init; } [JsonPropertyName("target_url")] public string? TargetUrl { get; init; } public string? Sha { get; init; } }
}
