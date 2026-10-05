using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Automaton.Core.Models;
using Automaton.Runner.Console.Services;
using NUnit.Framework;

namespace Automaton.Runner.Console.Tests;

public sealed class RunnerExecutionDeliveryTests
{
    private string directory = null!;
    [SetUp] public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "automaton-delivery-test-" + Guid.NewGuid().ToString("N"));
    [TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Test]
    public async Task InvalidKnownCommandBecomesTerminalAndNextCommandCompletes()
    {
        var http = new Api(); using var api = Client(http); var executor = new Executor(); var handler = Handler(executor);
        var badId = Guid.NewGuid(); var goodId = Guid.NewGuid(); var runnerId = Guid.NewGuid();
        Assert.That(await handler.HandleAsync(Encoding.UTF8.GetBytes("{"), badId.ToString(), api, runnerId, CancellationToken.None), Is.False);
        Assert.That(http.Results[badId].Outcome, Is.EqualTo("Failed"));
        Assert.That(await handler.HandleAsync(Body(goodId, runnerId), goodId.ToString(), api, runnerId, CancellationToken.None), Is.True);
        Assert.That(http.Results[goodId].Outcome, Is.EqualTo("Succeeded")); Assert.That(executor.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task TransientResultFailuresRetrySameResultWithoutRunningAgain()
    {
        var http = new Api { FailResults = 2 }; using var api = Client(http); var executor = new Executor();
        var id = Guid.NewGuid(); var runnerId = Guid.NewGuid(); var handler = Handler(executor);
        Assert.That(await handler.HandleAsync(Body(id, runnerId), id.ToString(), api, runnerId, CancellationToken.None), Is.True);
        Assert.That(http.Posts, Has.Count.EqualTo(3)); Assert.That(http.Posts.Distinct().Count(), Is.EqualTo(1));
        Assert.That(executor.Calls, Is.EqualTo(1));
        Assert.That(await handler.HandleAsync(Body(id, runnerId), id.ToString(), api, runnerId, CancellationToken.None), Is.True);
        Assert.That(executor.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task CancelBeforeDispatchDoesNotExecuteAndUnknownCommandDoesNotPoisonQueue()
    {
        var http = new Api { Cancel = true }; using var api = Client(http); var executor = new Executor(); var handler = Handler(executor);
        var id = Guid.NewGuid(); var runnerId = Guid.NewGuid();
        Assert.That(await handler.HandleAsync(Encoding.UTF8.GetBytes("{"), null, api, runnerId, CancellationToken.None), Is.False);
        Assert.That(await handler.HandleAsync(Body(id, runnerId), id.ToString(), api, runnerId, CancellationToken.None), Is.True);
        Assert.That(http.Results[id].Outcome, Is.EqualTo("Cancelled")); Assert.That(executor.Calls, Is.Zero);
    }

    [Test]
    public async Task WrongRunnerAddressNeverExecutesAndForeignOperationIsRejected()
    {
        var http = new Api { Forbidden = true }; using var api = Client(http); var executor = new Executor(); var handler = Handler(executor);
        Assert.That(await handler.HandleAsync(Body(Guid.NewGuid(), Guid.NewGuid()), null, api, Guid.NewGuid(), CancellationToken.None), Is.False);
        Assert.That(executor.Calls, Is.Zero);
    }

    [Test]
    public async Task RuntimeTunnelReconnectCommandRequestsReconnectWithoutCallingExecutionApi()
    {
        var http = new Api(); using var api = Client(http); var executor = new Executor();
        var requester = new ReconnectRequester(); var handler = Handler(executor, requester);
        var command = new RunnerExecutionCommand(Guid.NewGuid(), Guid.NewGuid(),
            RunnerExecutionCommandKinds.ReconnectRuntimeTunnels, "new-api-instance", "green");

        Assert.That(await handler.HandleAsync(
            JsonSerializer.SerializeToUtf8Bytes(command, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            command.OperationId.ToString("N"), api, command.RunnerId, CancellationToken.None), Is.True);
        Assert.That(requester.Requests, Is.EqualTo(1));
        Assert.That(requester.TargetApiInstanceId, Is.EqualTo("new-api-instance"));
        Assert.That(requester.TargetApiColor, Is.EqualTo("green"));
        Assert.That(executor.Calls, Is.Zero);
        Assert.That(http.Results, Is.Empty);
    }

    [Test]
    public async Task QueuedCutoverCommandsConvergeAfterRapidRollbackRollforward()
    {
        var http = new Api(); using var api = Client(http); var executor = new Executor();
        var requester = new ReconnectRequester(); var handler = Handler(executor, requester);
        var runnerId = Guid.NewGuid();
        requester.State.SetConnectedApiColor("green");
        foreach (var color in new[] { "green", "blue", "green" })
        {
            var command = new RunnerExecutionCommand(Guid.NewGuid(), runnerId,
                RunnerExecutionCommandKinds.ReconnectRuntimeTunnels, TargetApiColor: color);
            Assert.That(await handler.HandleAsync(JsonSerializer.SerializeToUtf8Bytes(command),
                command.OperationId.ToString(), api, runnerId, CancellationToken.None), Is.True);
            requester.State.SetConnectedApiColor(null);
            requester.State.Reset();
        }

        // The older connection finishes after the final queued command was applied.
        Assert.That(requester.State.SetConnectedApiColor("blue"), Is.True);
        Assert.That(requester.State.TryBeginTunnelRequest(), Is.False);
        Assert.That(requester.State.SetConnectedApiColor("green"), Is.False);
        Assert.That(requester.State.TryBeginTunnelRequest(), Is.True);
        Assert.That(http.Posts, Is.Empty);
        Assert.That(executor.Calls, Is.Zero);
    }

    private RunnerExecutionDeliveryHandler Handler(Executor executor, ReconnectRequester? requester = null)
        => new(new RunnerExecutionProcessor(new RunnerExecutionJournal(directory), executor), requester ?? new ReconnectRequester());
    private static RunnerExecutionApiClient Client(Api handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });
    private static byte[] Body(Guid id, Guid runnerId)
        => JsonSerializer.SerializeToUtf8Bytes(new RunnerExecutionCommand(id, runnerId), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private sealed class Executor : IRunnerExecutionExecutor
    {
        public int Calls { get; private set; }
        public Task<RunnerExecutionResult> ExecuteAsync(RunnerExecutionRequest request, CancellationToken token)
        { Calls++; return Task.FromResult(new RunnerExecutionResult { Outcome = "Succeeded", Output = "marker" }); }
    }
    private sealed class ReconnectRequester : IRuntimeTunnelReconnectRequester
    {
        public RunnerTunnelReconnectState State { get; } = new();
        public int Requests { get; private set; }
        public string? TargetApiInstanceId { get; private set; }
        public string? TargetApiColor { get; private set; }
        public void RequestRuntimeTunnelReconnect(string? targetApiInstanceId, string? targetApiColor, string? reason)
        {
            State.RequestReconnect(targetApiColor);
            Requests++;
            TargetApiInstanceId = targetApiInstanceId;
            TargetApiColor = targetApiColor;
        }
    }
    private sealed class Api : HttpMessageHandler
    {
        public Dictionary<Guid, RunnerExecutionResult> Results { get; } = [];
        public List<string> Posts { get; } = [];
        public int FailResults { get; init; }
        public bool Cancel { get; init; }
        public bool Forbidden { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Forbidden) return new(HttpStatusCode.Forbidden);
            var segments = request.RequestUri!.AbsolutePath.Split('/'); var id = Guid.Parse(segments[3]);
            if (request.Method == HttpMethod.Get)
            {
                var disposition = Results.ContainsKey(id) ? "already-applied" : "execute";
                if (Cancel && !Results.ContainsKey(id)) disposition = "cancel";
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new RunnerExecutionAssignment(
                    disposition, new(Guid.NewGuid(), null))) };
            }
            var body = await request.Content!.ReadAsStringAsync(cancellationToken); Posts.Add(body);
            if (Posts.Count <= FailResults)
            {
                var transient = new HttpResponseMessage(Posts.Count == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.ServiceUnavailable);
                transient.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero); return transient;
            }
            Results[id] = JsonSerializer.Deserialize<RunnerExecutionResult>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new RunnerExecutionReceipt("accepted")) };
        }
    }
}
