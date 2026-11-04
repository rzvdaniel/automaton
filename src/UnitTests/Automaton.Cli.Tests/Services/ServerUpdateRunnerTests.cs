using System.CommandLine;
using System.Net;
using System.Text;
using System.CommandLine.Parsing;
using System.Text.Json;
using Automaton.Cli.Commands;
using Automaton.Cli.Services;
using NUnit.Framework;

namespace Automaton.Cli.Tests.Services;

[TestFixture]
public class ServerUpdateRunnerTests
{
    private const string Update = "GET /api/install/update";
    private const string Health = "GET /health";
    private const string Summary = "GET /api/install/summary";
    private const string OldRun = """{"currentVersion":"server-v0.1.1","triggerAvailable":true,"run":{"state":"succeeded","startedAtUtc":"2026-11-01T10:00:00Z","toVersion":"server-v0.1.1"}}""";

    private static string Run(string state, string? to = null, int? exit = null, string? backup = null)
        => JsonSerializer.Serialize(new
        {
            currentVersion = "server-v0.1.1",
            triggerAvailable = true,
            run = new { state, startedAtUtc = "2026-11-04T10:00:00Z", toVersion = to, exitCode = exit, recoveryBackup = backup, message = state == "failed" ? "boom" : null },
            log = "pulling images\nfailed to start api"
        });

    [Test]
    public async Task WithoutWaitItOnlyRequestsTheUpdate()
    {
        var server = new FakeServer().On(Update, OldRun);

        var outcome = await RunAsync(server, "latest", wait: false);

        Assert.That(outcome.Success, Is.True);
        Assert.That(server.Posted.Single(), Is.EqualTo("""{"version":"latest","allowDowngrade":false}"""));
    }

    [Test]
    public async Task WaitFollowsTheRunThroughApiRestartsAndVerifiesTheNewVersion()
    {
        var server = new FakeServer()
            .On(Update, OldRun,                           // before the request
                OldRun,                                   // the host has not picked the request up yet
                Run("running"),
                HttpStatusCode.BadGateway,                // the API container is being replaced
                Run("succeeded", "server-v0.2.0", 0))
            .On(Health, "ok")
            .On(Summary, """{"version":"server-v0.1.1"}""", // the old API still answering
                """{"version":"server-v0.2.0"}""");

        var outcome = await RunAsync(server, "latest", wait: true);

        Assert.That(outcome.Success, Is.True, outcome.Message);
        Assert.That(outcome.Message, Does.Contain("server-v0.2.0"));
    }

    [Test]
    public async Task WaitAcceptsThePlainVersionThatInstallationStatusReports()
    {
        // The real API reports "0.2.0" in /api/install/summary while the update status says "server-v0.2.0".
        var server = new FakeServer()
            .On(Update, OldRun, Run("succeeded", "server-v0.2.0", 0))
            .On(Health, "ok")
            .On(Summary, """{"version":"0.2.0"}""");

        var outcome = await RunAsync(server, "latest", wait: true);

        Assert.That(outcome.Success, Is.True, outcome.Message);
    }

    [Test]
    public async Task WaitReportsFailureWithLogAndRecoveryBackup()
    {
        var server = new FakeServer().On(Update, OldRun,
            Run("failed", "server-v0.1.1", 1, "/opt/automaton-server/backups/server-v0.2.0-x"));

        var outcome = await RunAsync(server, "server-v0.2.0", wait: true);

        Assert.That(outcome.Success, Is.False);
        Assert.That(outcome.Message, Does.Contain("/opt/automaton-server/backups/server-v0.2.0-x"));
        Assert.That(outcome.Message, Does.Contain("boom"));
        Assert.That(outcome.Log, Does.Contain("failed to start api"));
    }

    [Test]
    public async Task WaitFailsWhenTheInstalledVersionIsNotTheRequestedOne()
    {
        var server = new FakeServer().On(Update, OldRun, Run("succeeded", "server-v0.1.5", 0));

        var outcome = await RunAsync(server, "server-v0.2.0", wait: true);

        Assert.That(outcome.Success, Is.False);
        Assert.That(outcome.Message, Does.Contain("server-v0.1.5"));
    }

    [Test]
    public async Task WaitFailsWhenTheApiKeepsReportingTheOldVersionAfterSuccess()
    {
        var server = new FakeServer()
            .On(Update, OldRun, Run("succeeded", "server-v0.2.0", 0))
            .On(Health, "ok")
            .On(Summary, """{"version":"server-v0.1.1"}""");

        var outcome = await RunAsync(server, "latest", wait: true);

        Assert.That(outcome.Success, Is.False);
        Assert.That(outcome.Message, Does.Contain("instead of server-v0.2.0"));
    }

    [Test]
    public async Task RefusesToStartWhileAnotherUpdateRuns()
    {
        var server = new FakeServer().On(Update, Run("running"));

        var outcome = await RunAsync(server, "latest", wait: true);

        Assert.That(outcome.Success, Is.False);
        Assert.That(server.Posted, Is.Empty);
    }

    [Test]
    public async Task RefusesToStartWhileAnUpdateIsStarting()
    {
        var server = new FakeServer().On(Update, Run("starting"));

        var outcome = await RunAsync(server, "latest", wait: false);

        Assert.That(outcome.Success, Is.False);
        Assert.That(server.Posted, Is.Empty);
    }

    [Test]
    public async Task StartsAgainWhenTheRunningStatusIsMarkedStaleByTheServer()
    {
        var stale = Run("running").Replace("\"triggerAvailable\":true", "\"triggerAvailable\":true,\"stale\":true");
        var server = new FakeServer().On(Update, stale);

        var outcome = await RunAsync(server, "latest", wait: false);

        Assert.That(outcome.Success, Is.True, outcome.Message);
        Assert.That(server.Posted, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task WaitFailsEarlyWithTheHintWhenTheHostDoesNotPickTheRequestUp()
    {
        var hint = "The server has not picked up the update request. On the server, check: systemctl status automaton-update.path";
        var pending = OldRun.Replace("\"triggerAvailable\":true", $"\"triggerAvailable\":true,\"requestPending\":true,\"requestAgeSeconds\":400,\"hint\":\"{hint}\"");
        var server = new FakeServer().On(Update, OldRun, pending);

        var outcome = await RunAsync(server, "latest", wait: true);

        Assert.That(outcome.Success, Is.False);
        Assert.That(outcome.Message, Does.Contain("systemctl status automaton-update.path"));
    }

    [Test]
    public async Task AllowDowngradeIsSentToTheServer()
    {
        var server = new FakeServer().On(Update, OldRun);

        await RunAsync(server, "server-v0.1.0", wait: false, allowDowngrade: true);

        Assert.That(server.Posted.Single(), Does.Contain("\"allowDowngrade\":true"));
    }

    [Test]
    public async Task WaitTimesOutWhenTheHostNeverReportsAFinishedRun()
    {
        var server = new FakeServer().On(Update, OldRun);

        var outcome = await RunAsync(server, "latest", wait: true, timeout: TimeSpan.FromMilliseconds(200));

        Assert.That(outcome.Success, Is.False);
        Assert.That(outcome.Message, Does.Contain("did not finish"));
    }

    [TestCase(null, "latest")]
    [TestCase("latest", "latest")]
    [TestCase("0.2.0", "server-v0.2.0")]
    [TestCase("server-v0.2.0", "server-v0.2.0")]
    [TestCase("v0.2.0", null)]
    [TestCase("server-v0.2", null)]
    [TestCase("latest; reboot", null)]
    public void NormalizesVersions(string? input, string? expected)
        => Assert.That(ServerUpdateRunner.NormalizeVersion(input), Is.EqualTo(expected));

    [Test]
    public void ServerCommandHasUpdateAndUpdateStatus()
    {
        var command = ServerCommand.Build(new ConfigService());
        var result = command.Parse("update --version 0.2.0 --wait --timeout 60 --json");

        Assert.That(result.Errors, Is.Empty);
        Assert.That(command.Subcommands.Select(child => child.Name), Is.EquivalentTo(new[] { "update", "update-status" }));
        Assert.That(command.Parse("update-status --json").Errors, Is.Empty);
    }

    private static async Task<ServerUpdateOutcome> RunAsync(FakeServer server, string version, bool wait, TimeSpan? timeout = null, bool allowDowngrade = false)
    {
        using var http = new HttpClient(server) { BaseAddress = new Uri("https://api.example.test/") };
        using var client = new AutomatonApiClient(http, "ak_test");
        var runner = new ServerUpdateRunner(client, (_, _) => Task.CompletedTask, _ => { }, TimeSpan.FromMilliseconds(100));
        return await runner.RunAsync(version, wait, timeout ?? TimeSpan.FromSeconds(30), CancellationToken.None, allowDowngrade);
    }

    /// <summary>Answers each route from its script; the last scripted answer repeats.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Dictionary<string, Queue<object>> scripts = [];
        public List<string> Posted { get; } = [];

        public FakeServer On(string route, params object[] responses)
        {
            scripts[route] = new(responses);
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Posted.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
            }
            var queue = scripts[$"{request.Method} {request.RequestUri!.AbsolutePath}"];
            var response = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            if (response is HttpStatusCode code) return new HttpResponseMessage(code) { Content = new StringContent("bad gateway") };
            var body = (string)response;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, body.StartsWith('{') ? "application/json" : "text/plain")
            };
        }
    }
}
