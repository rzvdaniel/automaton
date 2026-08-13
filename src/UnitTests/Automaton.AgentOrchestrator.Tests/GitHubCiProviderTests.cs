using System.Net;
using System.Text;
using Automaton.AgentOrchestrator.Ci;
using Automaton.AgentOrchestrator.Domain;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class GitHubCiProviderTests
{
    private static readonly string Sha = new('a', 40);
    private static readonly CiTarget Target = new("acme/widgets", 42, "automaton/work/7", Sha);

    [TestCase("queued", null, CiStatuses.Pending)]
    [TestCase("completed", "success", CiStatuses.Passed)]
    [TestCase("completed", "failure", CiStatuses.Failed)]
    [TestCase("completed", "cancelled", CiStatuses.Failed)]
    [TestCase("completed", "timed_out", CiStatuses.Failed)]
    [TestCase("completed", "neutral", CiStatuses.Indeterminate)]
    public async Task GetAsync_NormalizesExactRequiredCheck(string status, string? conclusion, string expected)
    {
        var handler = new StubHandler(Json(Pull()), Json(Checks(Check("build", status, conclusion, Sha))), Json(Statuses()));
        var snapshot = await Create(handler, ["build"]).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(expected)); Assert.That(snapshot.HeadSha, Is.EqualTo(Sha)); Assert.That(snapshot.Checks.Single().Name, Is.EqualTo("build")); });
    }

    [Test]
    public async Task GetAsync_MissingRequiredCheckIsPendingAndStaleShaIsIgnored()
    {
        var handler = new StubHandler(Json(Pull()), Json(Checks(Check("build", "completed", "success", new string('b', 40)))), Json(Statuses()));
        var snapshot = await Create(handler, ["build"]).GetAsync(Target, CancellationToken.None);
        Assert.That(snapshot.Status, Is.EqualTo(CiStatuses.Pending));
    }

    [Test]
    public void GetAsync_StaleHeadIsRetryableButOtherIdentityMismatchesAreNot()
    {
        var stale = new StubHandler(Json(Pull(new string('b', 40))));
        var wrongBranch = new StubHandler(Json(Pull(Sha).Replace("automaton/work/7", "other", StringComparison.Ordinal)));
        var wrongRepo = new StubHandler(Json(Pull(Sha).Replace("acme/widgets", "other/repo", StringComparison.Ordinal)));
        var closed = new StubHandler(Json(Pull(Sha).Replace("\"state\":\"open\"", "\"state\":\"closed\"", StringComparison.Ordinal)));

        var staleError = Assert.ThrowsAsync<CiProviderException>(() => Create(stale, []).GetAsync(Target, CancellationToken.None));
        var branchError = Assert.ThrowsAsync<CiProviderException>(() => Create(wrongBranch, []).GetAsync(Target, CancellationToken.None));
        var repoError = Assert.ThrowsAsync<CiProviderException>(() => Create(wrongRepo, []).GetAsync(Target, CancellationToken.None));
        var closedError = Assert.ThrowsAsync<CiProviderException>(() => Create(closed, []).GetAsync(Target, CancellationToken.None));
        Assert.Multiple(() => { Assert.That(staleError!.IsRetryable, Is.True); Assert.That(branchError!.IsRetryable, Is.False); Assert.That(repoError!.IsRetryable, Is.False); Assert.That(closedError!.IsRetryable, Is.False); });
    }

    [Test]
    public async Task GetAsync_FollowsSameOriginPaginationAndAggregatesMixedChecks()
    {
        var first = Json(Checks(Check("build", "completed", "success", Sha)), "<https://api.github.test/repos/acme/widgets/commits/x/check-runs?page=2>; rel=\"next\"");
        var handler = new StubHandler(Json(Pull()), first, Json(Checks(Check("test", "in_progress", null, Sha))), Json(Statuses()));
        var snapshot = await Create(handler, []).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(CiStatuses.Pending)); Assert.That(snapshot.Checks, Has.Count.EqualTo(2)); Assert.That(handler.Requests, Has.Count.EqualTo(4)); });
    }

    [Test]
    public void GetAsync_RejectsCrossOriginPaginationBeforeTokenForwarding()
    {
        var handler = new StubHandler(Json(Pull()), Json(Checks(Check("build", "completed", "success", Sha)), "<https://attacker.example/checks>; rel=\"next\""));
        Assert.ThrowsAsync<CiProviderException>(() => Create(handler, []).GetAsync(Target, CancellationToken.None));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [TestCase("success", "failure", CiStatuses.Failed)]
    [TestCase("success", "error", CiStatuses.Failed)]
    [TestCase("success", "pending", CiStatuses.Pending)]
    [TestCase("failure", "success", CiStatuses.Failed)]
    [TestCase("success", "success", CiStatuses.Passed)]
    [TestCase("success", "mystery", CiStatuses.Indeterminate)]
    public async Task GetAsync_AggregatesCheckRunsAndLegacyStatuses(string checkConclusion, string legacyState, string expected)
    {
        var handler = new StubHandler(
            Json(Pull()),
            Json(Checks(Check("build", "completed", checkConclusion, Sha))),
            Json(Statuses(Status("security-scan", legacyState, Sha))));
        var snapshot = await Create(handler, []).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(expected)); Assert.That(snapshot.Checks, Has.Count.EqualTo(2)); });
    }

    [TestCase("success", "failure", CiStatuses.Failed)]
    [TestCase("success", "pending", CiStatuses.Pending)]
    [TestCase("failure", "success", CiStatuses.Failed)]
    public async Task GetAsync_DuplicateNameUsesConservativeAggregate(string checkConclusion, string legacyState, string expected)
    {
        var handler = new StubHandler(Json(Pull()), Json(Checks(Check("build", "completed", checkConclusion, Sha))), Json(Statuses(Status("BUILD", legacyState, Sha))));
        var snapshot = await Create(handler, ["build"]).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(expected)); Assert.That(snapshot.Checks, Has.Count.EqualTo(2)); });
    }

    [Test]
    public async Task GetAsync_RequiredCheckCanMatchLegacyStatus()
    {
        var handler = new StubHandler(Json(Pull()), Json(Checks()), Json(Statuses(Status("security-scan", "success", Sha))));
        var snapshot = await Create(handler, ["SECURITY-SCAN"]).GetAsync(Target, CancellationToken.None);
        Assert.That(snapshot.Status, Is.EqualTo(CiStatuses.Passed));
    }

    [Test]
    public async Task GetAsync_MissingRequiredCheckAcrossBothSourcesIsPending()
    {
        var handler = new StubHandler(Json(Pull()), Json(Checks()), Json(Statuses()));
        var snapshot = await Create(handler, ["missing"]).GetAsync(Target, CancellationToken.None);
        Assert.That(snapshot.Status, Is.EqualTo(CiStatuses.Pending));
    }

    [Test]
    public async Task GetAsync_StaleLegacyStatusIsIgnored()
    {
        var handler = new StubHandler(Json(Pull()), Json(Checks()), Json(Statuses(Status("old", "failure", new string('b', 40)))));
        var snapshot = await Create(handler, []).GetAsync(Target, CancellationToken.None);
        Assert.That(snapshot.Status, Is.EqualTo(CiStatuses.Pending));
    }

    [TestCase("success", "failure", CiStatuses.Passed)]
    [TestCase("failure", "success", CiStatuses.Failed)]
    [TestCase("success", "pending", CiStatuses.Passed)]
    public async Task GetAsync_UsesNewestLegacyStatusForContext(string latestState, string olderState, string expected)
    {
        var handler = new StubHandler(
            Json(Pull()),
            Json(Checks()),
            Json(Statuses(Status("security-scan", latestState, Sha), Status("security-scan", olderState, Sha))));
        var snapshot = await Create(handler, []).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(expected)); Assert.That(snapshot.Checks, Has.Count.EqualTo(1)); });
    }

    [Test]
    public async Task GetAsync_CollapsesLegacyHistoryCaseInsensitivelyForRequiredCheck()
    {
        var handler = new StubHandler(
            Json(Pull()),
            Json(Checks()),
            Json(Statuses(Status("Security-Scan", "success", Sha), Status("SECURITY-SCAN", "failure", Sha))));
        var snapshot = await Create(handler, ["security-scan"]).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(CiStatuses.Passed)); Assert.That(snapshot.Checks, Has.Count.EqualTo(1)); });
    }

    [Test]
    public async Task GetAsync_NewestLegacyStatusOnFirstPageWinsAcrossPagination()
    {
        var first = Json(Statuses(Status("security-scan", "success", Sha)), "<https://api.github.test/repos/acme/widgets/commits/x/statuses?page=2>; rel=\"next\"");
        var handler = new StubHandler(Json(Pull()), Json(Checks()), first, Json(Statuses(Status("SECURITY-SCAN", "failure", Sha))));
        var snapshot = await Create(handler, []).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(CiStatuses.Passed)); Assert.That(snapshot.Checks, Has.Count.EqualTo(1)); });
    }

    [Test]
    public async Task GetAsync_EmptyRequiredListUsesOnlyLatestLegacyContexts()
    {
        var handler = new StubHandler(
            Json(Pull()),
            Json(Checks()),
            Json(Statuses(
                Status("build", "success", Sha),
                Status("build", "failure", Sha),
                Status("security-scan", "success", Sha),
                Status("security-scan", "pending", Sha))));
        var snapshot = await Create(handler, []).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(CiStatuses.Passed)); Assert.That(snapshot.Checks, Has.Count.EqualTo(2)); });
    }

    [Test]
    public async Task GetAsync_NoSignalsIsPending()
    {
        var snapshot = await Create(new StubHandler(Json(Pull()), Json(Checks()), Json(Statuses())), []).GetAsync(Target, CancellationToken.None);
        Assert.That(snapshot.Status, Is.EqualTo(CiStatuses.Pending));
    }

    [Test]
    public async Task GetAsync_FollowsSameOriginCommitStatusPagination()
    {
        var first = Json(Statuses(Status("build", "success", Sha)), "<https://api.github.test/repos/acme/widgets/commits/x/statuses?page=2>; rel=\"next\"");
        var handler = new StubHandler(Json(Pull()), Json(Checks()), first, Json(Statuses(Status("test", "success", Sha))));
        var snapshot = await Create(handler, []).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(CiStatuses.Passed)); Assert.That(snapshot.Checks, Has.Count.EqualTo(2)); Assert.That(handler.Requests, Has.Count.EqualTo(4)); });
    }

    [Test]
    public void GetAsync_RejectsCrossOriginCommitStatusPaginationBeforeTokenForwarding()
    {
        var handler = new StubHandler(Json(Pull()), Json(Checks()), Json(Statuses(Status("build", "success", Sha)), "<https://attacker.example/statuses>; rel=\"next\""));
        Assert.ThrowsAsync<CiProviderException>(() => Create(handler, []).GetAsync(Target, CancellationToken.None));
        Assert.That(handler.Requests, Has.Count.EqualTo(3));
    }

    [TestCase("[")]
    [TestCase(null)]
    public void GetAsync_RejectsMalformedOrOversizedCommitStatusResponse(string? malformed)
    {
        var body = malformed ?? new string('x', 256 * 1024 + 1);
        var handler = new StubHandler(Json(Pull()), Json(Checks()), Json(body));
        Assert.ThrowsAsync<CiProviderException>(() => Create(handler, []).GetAsync(Target, CancellationToken.None));
    }

    [TestCase(HttpStatusCode.Unauthorized, false)]
    [TestCase(HttpStatusCode.Forbidden, false)]
    [TestCase(HttpStatusCode.TooManyRequests, true)]
    [TestCase(HttpStatusCode.BadGateway, true)]
    public void GetAsync_ClassifiesCommitStatusErrors(HttpStatusCode status, bool retryable)
    {
        var error = Assert.ThrowsAsync<CiProviderException>(() => Create(new StubHandler(Json(Pull()), Json(Checks()), new HttpResponseMessage(status)), []).GetAsync(Target, CancellationToken.None));
        Assert.That(error!.IsRetryable, Is.EqualTo(retryable));
    }

    [Test]
    public void GetAsync_ClassifiesCommitStatusNetworkFailureAsRetryable()
    {
        var error = Assert.ThrowsAsync<CiProviderException>(() => Create(new SequenceHandler(Json(Pull()), Json(Checks())), []).GetAsync(Target, CancellationToken.None));
        Assert.That(error!.IsRetryable, Is.True);
    }

    [TestCase(HttpStatusCode.Unauthorized, false)]
    [TestCase(HttpStatusCode.Forbidden, false)]
    [TestCase(HttpStatusCode.TooManyRequests, true)]
    [TestCase(HttpStatusCode.InternalServerError, true)]
    public void GetAsync_ClassifiesProviderErrors(HttpStatusCode status, bool retryable)
    {
        var error = Assert.ThrowsAsync<CiProviderException>(() => Create(new StubHandler(new HttpResponseMessage(status)), []).GetAsync(Target, CancellationToken.None));
        Assert.That(error!.IsRetryable, Is.EqualTo(retryable));
    }

    [Test]
    public void GetAsync_RejectsUnexpectedPullRequestHead()
    {
        Assert.ThrowsAsync<CiProviderException>(() => Create(new StubHandler(Json(Pull(new string('b', 40)))), []).GetAsync(Target, CancellationToken.None));
    }

    private static GitHubCiProvider Create(HttpMessageHandler handler, IReadOnlyList<string> required) => new(new HttpClient(handler), new("acme", "widgets", "ci-token", new Uri("https://api.github.test/"), required));
    private static string Pull(string? sha = null) => $"{{\"state\":\"open\",\"draft\":true,\"head\":{{\"ref\":\"automaton/work/7\",\"sha\":\"{sha ?? Sha}\"}},\"base\":{{\"repo\":{{\"full_name\":\"acme/widgets\"}}}}}}";
    private static string Check(string name, string status, string? conclusion, string sha) => $"{{\"id\":1,\"name\":\"{name}\",\"status\":\"{status}\",\"conclusion\":{(conclusion is null ? "null" : "\"" + conclusion + "\"")},\"head_sha\":\"{sha}\",\"details_url\":\"https://github.test/check/1\",\"output\":{{\"summary\":\"bounded failure\"}}}}";
    private static string Checks(params string[] checks) => "{\"check_runs\":[" + string.Join(',', checks) + "]}";
    private static string Status(string context, string state, string sha) => $"{{\"id\":2,\"state\":\"{state}\",\"context\":\"{context}\",\"description\":\"bounded status\",\"target_url\":\"https://github.test/status/2\",\"sha\":\"{sha}\"}}";
    private static string Statuses(params string[] statuses) => "[" + string.Join(',', statuses) + "]";
    private static HttpResponseMessage Json(string body, string? link = null) { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }; if (link is not null) response.Headers.Add("Link", link); return response; }
    private sealed class StubHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);
        public List<(Uri Uri, string? Authorization)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString())); return Task.FromResult(queue.Dequeue()); }
    }
    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            queue.Count > 0 ? Task.FromResult(queue.Dequeue()) : throw new HttpRequestException("network failure");
    }
}
