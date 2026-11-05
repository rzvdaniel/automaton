using System.Net;
using System.Text;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class RunnerUpgradeRunnerTests
{
    private static string Status(string state, string message = "m") => $$"""{"runnerId":"r","state":"{{state}}","message":"{{message}}"}""";

    [TestCase("Reconnected", true)]
    [TestCase("RolledBack", false)]
    public async Task WaitEndsOnTheReportedOutcome(string final, bool success)
    {
        var server = new Fake(Status("InProgress"), HttpStatusCode.BadGateway, Status(final, "restored the previous container"));
        var outcome = await RunAsync(server, wait: true);
        Assert.That(outcome.Success, Is.EqualTo(success));
        Assert.That(outcome.State, Is.EqualTo(final));
        Assert.That(server.Started, Is.EqualTo(1));
    }

    [Test]
    public async Task WithoutWaitItOnlyStartsTheUpgrade()
    {
        var server = new Fake(Status("InProgress"));
        var outcome = await RunAsync(server, wait: false);
        Assert.That(outcome.Success, Is.True);
        Assert.That(server.Polled, Is.EqualTo(0));
    }

    [Test]
    public async Task WaitGivesUpAfterTheTimeout()
    {
        var outcome = await RunAsync(new Fake(Status("InProgress")), wait: true, timeout: TimeSpan.FromMilliseconds(-1));
        Assert.That(outcome.Success, Is.False);
    }

    private static async Task<RunnerUpgradeOutcome> RunAsync(Fake server, bool wait, TimeSpan? timeout = null)
    {
        using var http = new HttpClient(server) { BaseAddress = new Uri("https://api.example.test/") };
        using var client = new AutomatonApiClient(http, "ak_test");
        return await new RunnerUpgradeRunner(client, (_, _) => Task.CompletedTask, _ => { })
            .RunAsync("r", wait, timeout ?? TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    private sealed class Fake(params object[] polls) : HttpMessageHandler
    {
        private readonly Queue<object> queue = new(polls);
        public int Started { get; private set; }
        public int Polled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Started++;
                return Task.FromResult(Json(Status("InProgress"), HttpStatusCode.Accepted));
            }
            Polled++;
            var next = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return Task.FromResult(next is HttpStatusCode code ? new HttpResponseMessage(code) { Content = new StringContent("down") } : Json((string)next));
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
            => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
