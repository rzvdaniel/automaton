using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Publication;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class GitHubPullRequestPublisherTests
{
    private static readonly PullRequestPublicationRequest Request = new("acme/widgets", "automaton/work/7", new string('a', 40), "main", "Issue 7", "Refs #7");

    [Test]
    public async Task FindOrCreateAsync_CreatesExactDraftAndKeepsTokenHostSide()
    {
        var handler = new StubHandler(Json("[]"), Json("[]"), Json(PullJson()));
        var result = await Create(handler).FindOrCreateAsync(Request, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Draft, Is.True);
            Assert.That(result.HeadSha, Is.EqualTo(Request.HeadSha));
            Assert.That(handler.Requests, Has.Count.EqualTo(3));
            Assert.That(handler.Requests.All(x => x.Authorization == "Bearer host-token"), Is.True);
            Assert.That(handler.Requests[0].Uri.Query, Does.Contain("head=acme%3Aautomaton%2Fwork%2F7"));
            Assert.That(handler.Requests[0].Uri.Query, Does.Contain("base=main"));
            Assert.That(handler.Requests.All(x => x.ApiVersion == "2026-03-10"), Is.True);
            Assert.That(handler.Requests[2].Body, Does.Contain("\"draft\":true"));
            Assert.That(handler.Requests[2].Body, Does.Contain("\"head\":\"automaton/work/7\""));
        });
    }

    [Test]
    public async Task FindOrCreateAsync_ReusesOneExactOpenDraft()
    {
        var handler = new StubHandler(Json("[" + PullJson() + "]"));
        var result = await Create(handler).FindOrCreateAsync(Request, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(result.Number, Is.EqualTo(42)); Assert.That(handler.Requests, Has.Count.EqualTo(1)); });
    }

    [Test]
    public async Task FindExactAsync_VerifiesPersistedNumberAndReturnsAbsenceWithoutCreating()
    {
        var foundHandler = new StubHandler(Json("[" + PullJson() + "]"));
        var found = await Create(foundHandler).FindExactAsync(Request, 42, CancellationToken.None);
        var missingHandler = new StubHandler(Json("[]"));
        var missing = await Create(missingHandler).FindExactAsync(Request, 42, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(found!.Number, Is.EqualTo(42)); Assert.That(missing, Is.Null); Assert.That(missingHandler.Requests, Has.Count.EqualTo(1)); });
    }

    [Test]
    public void FindExactAsync_RejectsPersistedNumberMismatch()
    {
        var handler = new StubHandler(Json("[" + PullJson(43) + "]"));
        Assert.ThrowsAsync<PublicationException>(() => Create(handler).FindExactAsync(Request, 42, CancellationToken.None));
    }

    [Test]
    public void FindOrCreateAsync_RejectsDuplicateMatches()
    {
        var handler = new StubHandler(Json("[" + PullJson() + "," + PullJson(43) + "]"));
        Assert.ThrowsAsync<PublicationException>(() => Create(handler).FindOrCreateAsync(Request, CancellationToken.None));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void FindOrCreateAsync_RejectsCrossOriginPaginationBeforeCredentialsLeaveOrigin()
    {
        var handler = new StubHandler(Json("[]", "<https://attacker.example/pulls?page=2>; rel=\"next\""));
        var error = Assert.ThrowsAsync<PublicationException>(() => Create(handler).FindOrCreateAsync(Request, CancellationToken.None));
        Assert.Multiple(() => { Assert.That(error!.Message, Does.Not.Contain("host-token")); Assert.That(handler.Requests, Has.Count.EqualTo(1)); Assert.That(handler.Requests[0].Uri.Host, Is.EqualTo("api.github.test")); });
    }

    [TestCase(HttpStatusCode.Unauthorized, false)]
    [TestCase(HttpStatusCode.Forbidden, false)]
    [TestCase(HttpStatusCode.NotFound, false)]
    [TestCase(HttpStatusCode.Gone, false)]
    [TestCase(HttpStatusCode.InternalServerError, true)]
    [TestCase(HttpStatusCode.TooManyRequests, true)]
    public void FindOrCreateAsync_ClassifiesProviderFailures(HttpStatusCode status, bool retryable)
    {
        var error = Assert.ThrowsAsync<PublicationException>(() => Create(new StubHandler(new HttpResponseMessage(status))).FindOrCreateAsync(Request, CancellationToken.None));
        Assert.That(error!.IsRetryable, Is.EqualTo(retryable));
    }

    [Test]
    public void FindOrCreateAsync_RejectsUnexpectedCreatedIdentity()
    {
        var handler = new StubHandler(Json("[]"), Json("[]"), Json(PullJson(headSha: new string('b', 40))));
        Assert.ThrowsAsync<PublicationException>(() => Create(handler).FindOrCreateAsync(Request, CancellationToken.None));
    }

    [Test]
    public void FindOrCreateAsync_ClosedMatchingPullRequestRequiresManualAttention()
    {
        var closed = PullJson().Replace("\"state\":\"open\"", "\"state\":\"closed\"");
        var handler = new StubHandler(Json("[]"), Json("[" + closed + "]"));
        Assert.ThrowsAsync<PublicationException>(() => Create(handler).FindOrCreateAsync(Request, CancellationToken.None));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    private static GitHubPullRequestPublisher Create(StubHandler handler) => new(new HttpClient(handler), new("acme", "widgets", "host-token", new Uri("https://api.github.test/")));
    private static string PullJson(int number = 42, string? headSha = null)
        => $"{{\"number\":{number},\"state\":\"open\",\"draft\":true,\"html_url\":\"https://github.test/acme/widgets/pull/{number}\",\"head\":{{\"ref\":\"automaton/work/7\",\"sha\":\"{headSha ?? new string('a', 40)}\"}},\"base\":{{\"ref\":\"main\",\"repo\":{{\"full_name\":\"acme/widgets\"}}}}}}";
    private static HttpResponseMessage Json(string body, string? link = null) { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }; if (link is not null) response.Headers.Add("Link", link); return response; }

    private sealed class StubHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        public List<CapturedRequest> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), request.Headers.GetValues("X-GitHub-Api-Version").Single(), request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responses.Dequeue();
        }
    }
    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string ApiVersion, string? Body);
}
