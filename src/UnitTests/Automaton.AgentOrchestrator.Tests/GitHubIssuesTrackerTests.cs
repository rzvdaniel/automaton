using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Trackers.GitHub;
using Automaton.AgentOrchestrator.Workflows;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class GitHubIssuesTrackerTests
{
    [Test]
    public async Task DiscoverCandidatesAsync_FollowsPaginationAndNormalizesEligibleIssues()
    {
        var handler = new StubHandler(
            JsonResponse("""
            [
              {"number": 4, "state":"open", "title":"ready", "body":"details", "html_url":"https://github.com/acme/widgets/issues/4", "labels":[{"name":" Agent:Ready "},{"name":"Bug"},{"name":"bug"}], "assignee":{"login":"octo"}, "created_at":"2026-01-01T00:00:00Z", "updated_at":"2026-01-02T00:00:00Z"},
              {"number": 5, "state":"open", "title":"pull", "pull_request":{}, "labels":[{"name":"agent:ready"}]},
              {"number": 6, "state":"open", "title":"not opted in", "labels":[{"name":"bug"}]},
              {"number": 8, "state":"closed", "title":"terminal", "labels":[{"name":"agent:ready"}]}
            ]
            """, "<https://api.github.test/repos/acme/widgets/issues?state=open&per_page=100&page=2>; rel=\"next\""),
            JsonResponse("""
            [{"number":7,"state":"open","title":"second","labels":[{"name":"agent:ready"}]}]
            """));
        var tracker = Create(handler);

        var items = await tracker.DiscoverCandidatesAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(items.Select(x => x.Identifier), Is.EqualTo(new[] { "acme/widgets#4", "acme/widgets#7" }));
            Assert.That(items[0].Id, Is.EqualTo("github-issue:acme/widgets:4"));
            Assert.That(items[0].ProviderLifecycleState, Is.EqualTo("open"));
            Assert.That(items[0].Labels, Is.EqualTo(new[] { "agent:ready", "bug" }));
            Assert.That(items[0].AssignedTo, Is.EqualTo("octo"));
            Assert.That(handler.Requests, Has.Count.EqualTo(2));
            Assert.That(handler.Requests[0].Headers.Authorization, Is.EqualTo(new AuthenticationHeaderValue("Bearer", "host-token")));
            Assert.That(handler.Requests[0].RequestUri!.Query, Does.Contain("per_page=100"));
        });
    }

    [Test]
    public async Task GetByIdAsync_RefreshesIssueAndTreatsNotFoundAndGoneAsAbsent()
    {
        var handler = new StubHandler(
            JsonResponse("""
            {"number":9,"state":"closed","title":"closed","labels":[{"name":"agent:ready"}]}
            """),
            new HttpResponseMessage(HttpStatusCode.NotFound),
            new HttpResponseMessage(HttpStatusCode.Gone));
        var tracker = Create(handler);

        var refreshed = await tracker.GetByIdAsync("github-issue:acme/widgets:9", CancellationToken.None);
        var notFound = await tracker.GetByIdAsync("github-issue:acme/widgets:10", CancellationToken.None);
        var gone = await tracker.GetByIdAsync("github-issue:acme/widgets:11", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(refreshed!.ProviderLifecycleState, Is.EqualTo("closed"));
            Assert.That(notFound, Is.Null);
            Assert.That(gone, Is.Null);
            Assert.That(handler.Requests[0].RequestUri!.AbsolutePath, Is.EqualTo("/repos/acme/widgets/issues/9"));
        });
    }

    [Test]
    public void GetByIdAsync_RejectsOpaqueIdsFromAnotherTracker()
    {
        var handler = new StubHandler();
        var tracker = Create(handler);

        var item = tracker.GetByIdAsync("not-a-github-id", CancellationToken.None).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(item, Is.Null);
            Assert.That(handler.Requests, Is.Empty);
        });
    }

    [Test]
    public void DiscoverCandidatesAsync_ReportsAuthenticationFailureWithoutTokenValue()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var tracker = Create(handler);

        var exception = Assert.ThrowsAsync<GitHubTrackerException>(async () => await tracker.DiscoverCandidatesAsync(CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(exception.Message, Does.Not.Contain("host-token"));
        });
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.Gone)]
    public void DiscoverCandidatesAsync_TreatsRepositoryAbsenceAsTrackerFailure(HttpStatusCode statusCode)
    {
        var tracker = Create(new StubHandler(new HttpResponseMessage(statusCode)));

        var exception = Assert.ThrowsAsync<GitHubTrackerException>(async () => await tracker.DiscoverCandidatesAsync(CancellationToken.None));

        Assert.That(exception!.StatusCode, Is.EqualTo(statusCode));
    }

    [Test]
    public void DiscoverCandidatesAsync_RejectsCrossOriginPaginationBeforeSendingCredentials()
    {
        var handler = new StubHandler(JsonResponse("""
            [{"number":4,"state":"open","title":"ready","labels":[{"name":"agent:ready"}]}]
            """, "<https://attacker.example/issues?page=2>; rel=\"next\""));
        var tracker = Create(handler);

        var exception = Assert.ThrowsAsync<GitHubTrackerProtocolException>(async () => await tracker.DiscoverCandidatesAsync(CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Not.Contain("host-token"));
            Assert.That(handler.Requests, Has.Count.EqualTo(1));
            Assert.That(handler.Requests[0].RequestUri!.Host, Is.EqualTo("api.github.test"));
        });
    }

    [Test]
    public void Options_RequireExplicitRepositoryScopeAndHostToken()
    {
        var options = new TrackerOptions
        {
            Kind = GitHubIssuesTrackerOptions.TrackerKind,
            RequiredLabels = ["agent:ready"],
            Provider = new Dictionary<string, string> { ["owner"] = "acme", ["repository"] = "widgets" }
        };

        var exception = Assert.Throws<InvalidOperationException>(() => GitHubIssuesTrackerOptions.From(options));

        Assert.That(exception!.Message, Is.EqualTo("tracker.provider.token is required for GitHub Issues."));
    }

    [Test]
    public void Options_RequireAnOptInLabel()
    {
        var options = new TrackerOptions
        {
            Kind = GitHubIssuesTrackerOptions.TrackerKind,
            Provider = new Dictionary<string, string> { ["owner"] = "acme", ["repository"] = "widgets", ["token"] = "host-token" }
        };

        var exception = Assert.Throws<InvalidOperationException>(() => GitHubIssuesTrackerOptions.From(options));

        Assert.That(exception!.Message, Is.EqualTo("tracker.required_labels must contain at least one opt-in label for GitHub Issues."));
    }

    private static GitHubIssuesTracker Create(StubHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test/") };
        var options = new GitHubIssuesTrackerOptions("acme", "widgets", "host-token", ["agent:ready"], client.BaseAddress);
        return new GitHubIssuesTracker(client, options);
    }

    private static HttpResponseMessage JsonResponse(string json, string? link = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (link is not null) response.Headers.Add("Link", link);
        return response;
    }

    private sealed class StubHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responses.Dequeue());
        }
    }
}
