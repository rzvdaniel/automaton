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
        var handler = new StubHandler(Json(Pull()), Json(Checks(Check("build", status, conclusion, Sha))));
        var snapshot = await Create(handler, ["build"]).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(expected)); Assert.That(snapshot.HeadSha, Is.EqualTo(Sha)); Assert.That(snapshot.Checks.Single().Name, Is.EqualTo("build")); });
    }

    [Test]
    public async Task GetAsync_MissingRequiredCheckIsPendingAndStaleShaIsIgnored()
    {
        var handler = new StubHandler(Json(Pull()), Json(Checks(Check("build", "completed", "success", new string('b', 40)))));
        var snapshot = await Create(handler, ["build"]).GetAsync(Target, CancellationToken.None);
        Assert.That(snapshot.Status, Is.EqualTo(CiStatuses.Pending));
    }

    [Test]
    public async Task GetAsync_FollowsSameOriginPaginationAndAggregatesMixedChecks()
    {
        var first = Json(Checks(Check("build", "completed", "success", Sha)), "<https://api.github.test/repos/acme/widgets/commits/x/check-runs?page=2>; rel=\"next\"");
        var handler = new StubHandler(Json(Pull()), first, Json(Checks(Check("test", "in_progress", null, Sha))));
        var snapshot = await Create(handler, []).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(CiStatuses.Pending)); Assert.That(snapshot.Checks, Has.Count.EqualTo(2)); Assert.That(handler.Requests, Has.Count.EqualTo(3)); });
    }

    [Test]
    public void GetAsync_RejectsCrossOriginPaginationBeforeTokenForwarding()
    {
        var handler = new StubHandler(Json(Pull()), Json(Checks(Check("build", "completed", "success", Sha)), "<https://attacker.example/checks>; rel=\"next\""));
        Assert.ThrowsAsync<CiProviderException>(() => Create(handler, []).GetAsync(Target, CancellationToken.None));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
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

    private static GitHubCiProvider Create(StubHandler handler, IReadOnlyList<string> required) => new(new HttpClient(handler), new("acme", "widgets", "ci-token", new Uri("https://api.github.test/"), required));
    private static string Pull(string? sha = null) => $"{{\"state\":\"open\",\"draft\":true,\"head\":{{\"ref\":\"automaton/work/7\",\"sha\":\"{sha ?? Sha}\"}},\"base\":{{\"repo\":{{\"full_name\":\"acme/widgets\"}}}}}}";
    private static string Check(string name, string status, string? conclusion, string sha) => $"{{\"id\":1,\"name\":\"{name}\",\"status\":\"{status}\",\"conclusion\":{(conclusion is null ? "null" : "\"" + conclusion + "\"")},\"head_sha\":\"{sha}\",\"details_url\":\"https://github.test/check/1\",\"output\":{{\"summary\":\"bounded failure\"}}}}";
    private static string Checks(params string[] checks) => "{\"check_runs\":[" + string.Join(',', checks) + "]}";
    private static HttpResponseMessage Json(string body, string? link = null) { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }; if (link is not null) response.Headers.Add("Link", link); return response; }
    private sealed class StubHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);
        public List<(Uri Uri, string? Authorization)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString())); return Task.FromResult(queue.Dequeue()); }
    }
}
