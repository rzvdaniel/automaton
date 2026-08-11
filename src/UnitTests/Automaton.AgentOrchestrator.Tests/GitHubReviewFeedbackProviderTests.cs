using System.Net;
using System.Text;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.ReviewFeedback;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class GitHubReviewFeedbackProviderTests
{
    private static readonly string Sha = new('a', 40);
    private static readonly ReviewFeedbackTarget Target = new("acme/widgets", 42, "automaton/work/7", Sha);

    [Test]
    public async Task ExactHumanReviewAndInlineCommentAreActionable()
    {
        var handler = new StubHandler(Json(Pull()), Json(Reviews(Review(1, "alice", "User", "CHANGES_REQUESTED", Sha))), Json(Comments(Comment(2, "bob", "User", Sha))));
        var snapshot = await Create(handler).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(ReviewFeedbackStatuses.Actionable)); Assert.That(snapshot.Items.Select(x => x.Kind), Is.EquivalentTo(new[] { ReviewFeedbackKinds.ChangesRequested, ReviewFeedbackKinds.ReviewComment })); });
    }

    [Test]
    public async Task LatestReviewPerHumanOnExactHeadDeterminesApproval()
    {
        var handler = new StubHandler(Json(Pull()), Json(Reviews(Review(1, "alice", "User", "CHANGES_REQUESTED", Sha), Review(2, "ALICE", "User", "APPROVED", Sha))), Json(Comments()));
        var snapshot = await Create(handler).GetAsync(Target, CancellationToken.None);
        Assert.That(snapshot.Status, Is.EqualTo(ReviewFeedbackStatuses.Approved));
    }

    [Test]
    public async Task BotsAutomationAndOtherShaAreIgnored()
    {
        var old = new string('b', 40);
        var handler = new StubHandler(Json(Pull()), Json(Reviews(Review(1, "ci[bot]", "Bot", "CHANGES_REQUESTED", Sha), Review(2, "automaton", "User", "CHANGES_REQUESTED", Sha), Review(3, "alice", "User", "CHANGES_REQUESTED", old))), Json(Comments(Comment(4, "bot[bot]", "Bot", Sha))));
        var snapshot = await Create(handler).GetAsync(Target, CancellationToken.None);
        Assert.Multiple(() => { Assert.That(snapshot.Status, Is.EqualTo(ReviewFeedbackStatuses.AwaitingHuman)); Assert.That(snapshot.Items, Is.Empty); });
    }

    [Test]
    public void PullHeadMismatchFailsClosed()
        => Assert.ThrowsAsync<ReviewFeedbackProviderException>(() => Create(new StubHandler(Json(Pull(new string('b', 40))))).GetAsync(Target, CancellationToken.None));

    [Test]
    public void CrossOriginPaginationIsRejectedBeforeCredentialForwarding()
    {
        var handler = new StubHandler(Json(Pull()), Json(Reviews(), "<https://attacker.example/reviews>; rel=\"next\""));
        Assert.ThrowsAsync<ReviewFeedbackProviderException>(() => Create(handler).GetAsync(Target, CancellationToken.None));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [TestCase(HttpStatusCode.Unauthorized, false)]
    [TestCase(HttpStatusCode.Forbidden, false)]
    [TestCase(HttpStatusCode.TooManyRequests, true)]
    [TestCase(HttpStatusCode.BadGateway, true)]
    public void ProviderErrorsAreClassified(HttpStatusCode status, bool retryable)
    {
        var error = Assert.ThrowsAsync<ReviewFeedbackProviderException>(() => Create(new StubHandler(new HttpResponseMessage(status))).GetAsync(Target, CancellationToken.None));
        Assert.That(error!.IsRetryable, Is.EqualTo(retryable));
    }

    [Test]
    public void MalformedAndOversizedResponsesFailClosed()
    {
        Assert.ThrowsAsync<ReviewFeedbackProviderException>(() => Create(new StubHandler(Json(Pull()), Json("["))).GetAsync(Target, CancellationToken.None));
        Assert.ThrowsAsync<ReviewFeedbackProviderException>(() => Create(new StubHandler(Json(Pull()), Json(new string('x', 256 * 1024 + 1)))).GetAsync(Target, CancellationToken.None));
    }

    [Test]
    public void NetworkFailureIsRetryable()
    {
        var error = Assert.ThrowsAsync<ReviewFeedbackProviderException>(() => Create(new SequenceHandler()).GetAsync(Target, CancellationToken.None));
        Assert.That(error!.IsRetryable, Is.True);
    }

    private static GitHubReviewFeedbackProvider Create(HttpMessageHandler handler) => new(new HttpClient(handler), new("acme", "widgets", "token", new Uri("https://api.github.test/"), "automaton"));
    private static string Pull(string? sha = null) => $"{{\"state\":\"open\",\"head\":{{\"ref\":\"automaton/work/7\",\"sha\":\"{sha ?? Sha}\"}},\"base\":{{\"repo\":{{\"full_name\":\"acme/widgets\"}}}}}}";
    private static string Review(long id, string login, string type, string state, string sha) => $"{{\"id\":{id},\"state\":\"{state}\",\"body\":\"fix this\",\"commit_id\":\"{sha}\",\"html_url\":\"https://github.test/review/{id}\",\"user\":{{\"login\":\"{login}\",\"type\":\"{type}\"}}}}";
    private static string Comment(long id, string login, string type, string sha) => $"{{\"id\":{id},\"body\":\"inline fix\",\"path\":\"src/a.cs\",\"line\":10,\"commit_id\":\"{sha}\",\"html_url\":\"https://github.test/comment/{id}\",\"user\":{{\"login\":\"{login}\",\"type\":\"{type}\"}}}}";
    private static string Reviews(params string[] values) => "[" + string.Join(',', values) + "]";
    private static string Comments(params string[] values) => "[" + string.Join(',', values) + "]";
    private static HttpResponseMessage Json(string body, string? link = null) { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }; if (link is not null) response.Headers.Add("Link", link); return response; }
    private sealed class StubHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> queue = new(responses);
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Requests.Add(request.RequestUri!); return Task.FromResult(queue.Dequeue()); }
    }
    private sealed class SequenceHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new HttpRequestException("network");
    }
}
