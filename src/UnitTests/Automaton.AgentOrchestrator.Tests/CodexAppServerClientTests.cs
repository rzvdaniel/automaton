using System.Text.Json;
using System.Threading.Channels;
using Automaton.AgentOrchestrator.Codex;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using NUnit.Framework;

namespace Automaton.AgentOrchestrator.Tests;

[TestFixture]
public sealed class CodexAppServerClientTests
{
    private static readonly string WorkspacePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));

    [Test]
    public async Task Session_InitializesStartsThreadAndTurnAndNormalizesNotifications()
    {
        var transport = new ScriptedTransport();
        transport.OnSend = message => RespondNormally(transport, message);
        var ownership = new RecordingOwnership();
        await using var client = new CodexAppServerClient(transport, ownership, new SystemClock(), TimeSpan.FromSeconds(2), WorkspacePath);

        await client.InitializeAsync(CancellationToken.None);
        var thread = await client.StartThreadAsync(CancellationToken.None);
        var turn = await client.StartTurnAsync(thread.Id, "Do not modify files.", CancellationToken.None);
        transport.Emit("""{"method":"item/started","params":{"threadId":"thread-1","turnId":"turn-1","item":{"id":"item-1","type":"agentMessage"},"startedAtMs":1}}""");
        transport.Emit("""{"method":"thread/tokenUsage/updated","params":{"threadId":"thread-1","turnId":"turn-1","tokenUsage":{"total":{"inputTokens":12,"outputTokens":5}}}}""");
        transport.Emit("""{"method":"turn/completed","params":{"threadId":"thread-1","turn":{"id":"turn-1","status":"completed","items":[]}}}""");

        var result = await client.WaitForTurnCompletionAsync(turn, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo("completed"));
            Assert.That(thread.Id, Is.EqualTo("thread-1"));
            Assert.That(turn.Id, Is.EqualTo("turn-1"));
            Assert.That(client.Events, Has.Some.Matches<CodexSessionEvent>(x => x.Kind == CodexSessionEventKinds.Initialized));
            Assert.That(client.Events, Has.Some.Matches<CodexSessionEvent>(x => x.Kind == CodexSessionEventKinds.ItemActivity && x.TurnId == "turn-1"));
            Assert.That(client.Events, Has.Some.Matches<CodexSessionEvent>(x => x.Kind == CodexSessionEventKinds.UsageUpdated && x.InputTokens == 12 && x.OutputTokens == 5));
            Assert.That(transport.Methods.Take(2), Is.EqualTo(new[] { "initialize", "initialized" }));
            Assert.That(ownership.AcquiredNonce, Is.EqualTo(transport.LaunchNonce));
        });
    }

    [Test]
    public async Task Requests_AreCorrelatedWhenResponsesArriveOutOfOrder()
    {
        var transport = new ScriptedTransport();
        var held = new List<(long Id, string Method)>();
        transport.OnSend = message =>
        {
            var (id, method) = Request(message);
            if (method == "initialize") transport.Respond(id!.Value, new { });
            else if (method == "initialized") { }
            else
            {
                held.Add((id!.Value, method));
                if (held.Count == 2)
                    foreach (var request in held.AsEnumerable().Reverse()) transport.Respond(request.Id, new { thread = new { id = request.Method == "thread/start" ? "started" : "resumed" }, cwd = WorkspacePath });
            }
        };
        await using var client = new CodexAppServerClient(transport, new RecordingOwnership(), new SystemClock(), TimeSpan.FromSeconds(2), WorkspacePath);
        await client.InitializeAsync(CancellationToken.None);

        var startedTask = client.StartThreadAsync(CancellationToken.None);
        var resumedTask = client.ResumeThreadAsync("existing", CancellationToken.None);
        var started = await startedTask;
        var resumed = await resumedTask;

        Assert.Multiple(() =>
        {
            Assert.That(started.Id, Is.EqualTo("started"));
            Assert.That(resumed.Id, Is.EqualTo("resumed"));
        });
    }

    [Test]
    public async Task ThreadStartAndResume_AreBoundToTheAssignedWorkspace()
    {
        var transport = new ScriptedTransport();
        transport.OnSend = message => RespondNormally(transport, message);
        await using var client = new CodexAppServerClient(transport, new RecordingOwnership(), new SystemClock(), TimeSpan.FromSeconds(2), WorkspacePath);
        await client.InitializeAsync(CancellationToken.None);

        await client.StartThreadAsync(CancellationToken.None);
        await client.ResumeThreadAsync("existing", CancellationToken.None);

        var requests = transport.Messages.Select(ParseMessage).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(requests.Single(x => x.Method == "thread/start").Cwd, Is.EqualTo(WorkspacePath));
            Assert.That(requests.Single(x => x.Method == "thread/resume").Cwd, Is.EqualTo(WorkspacePath));
            Assert.That(typeof(ICodexAppServerClient).GetMethod(nameof(ICodexAppServerClient.StartThreadAsync))!.GetParameters(), Has.Length.EqualTo(1), "the public contract has no caller-supplied cwd");
        });
    }

    [Test]
    public async Task ResumeThread_RejectsAWorkspaceMismatch()
    {
        var outside = Path.GetFullPath(Path.Combine(WorkspacePath, "..", "outside-workspace"));
        var transport = new ScriptedTransport();
        transport.OnSend = message =>
        {
            var (id, method) = Request(message);
            if (method == "initialize") transport.Respond(id!.Value, new { });
            else if (method == "thread/resume") transport.Respond(id!.Value, new { thread = new { id = "thread-1" }, cwd = outside });
        };
        await using var client = new CodexAppServerClient(transport, new RecordingOwnership(), new SystemClock(), TimeSpan.FromSeconds(2), WorkspacePath);
        await client.InitializeAsync(CancellationToken.None);

        Assert.ThrowsAsync<CodexProtocolException>(async () => await client.ResumeThreadAsync("existing", CancellationToken.None));
    }

    [Test]
    public async Task StartThread_RejectsAWorkspaceMismatch()
    {
        var outside = Path.GetFullPath(Path.Combine(WorkspacePath, "..", "outside-workspace"));
        var transport = new ScriptedTransport();
        transport.OnSend = message =>
        {
            var (id, method) = Request(message);
            if (method == "initialize") transport.Respond(id!.Value, new { });
            else if (method == "thread/start") transport.Respond(id!.Value, new { thread = new { id = "thread-1" }, cwd = outside });
        };
        await using var client = new CodexAppServerClient(transport, new RecordingOwnership(), new SystemClock(), TimeSpan.FromSeconds(2), WorkspacePath);
        await client.InitializeAsync(CancellationToken.None);

        Assert.ThrowsAsync<CodexProtocolException>(async () => await client.StartThreadAsync(CancellationToken.None));
    }

    [TestCase(false, null)]
    [TestCase(false, "relative-workspace")]
    [TestCase(true, null)]
    [TestCase(true, "relative-workspace")]
    public async Task ThreadResponse_RejectsMissingOrRelativeWorkspace(bool resume, string? returnedCwd)
    {
        var transport = new ScriptedTransport();
        transport.OnSend = message =>
        {
            var (id, method) = Request(message);
            if (method == "initialize") transport.Respond(id!.Value, new { });
            else if (method == "thread/start" || method == "thread/resume")
                transport.Respond(id!.Value, new { thread = new { id = "thread-1" }, cwd = returnedCwd });
        };
        await using var client = new CodexAppServerClient(transport, new RecordingOwnership(), new SystemClock(), TimeSpan.FromSeconds(2), WorkspacePath);
        await client.InitializeAsync(CancellationToken.None);

        Assert.ThrowsAsync<CodexProtocolException>(async () =>
        {
            if (resume) await client.ResumeThreadAsync("existing", CancellationToken.None);
            else await client.StartThreadAsync(CancellationToken.None);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ProtocolFailures_AreVisibleAndQuarantineOwnership(bool malformed)
    {
        var transport = new ScriptedTransport();
        var ownership = new RecordingOwnership();
        transport.OnSend = message =>
        {
            var (id, method) = Request(message);
            if (method != "initialize") return;
            if (malformed) transport.Emit("not-json");
            else transport.Emit(JsonSerializer.Serialize(new { id, error = new { code = -32600, message = "bad request" } }));
        };
        var client = new CodexAppServerClient(transport, ownership, new SystemClock(), TimeSpan.FromSeconds(2), WorkspacePath);

        Assert.ThrowsAsync<CodexProtocolException>(async () => await client.InitializeAsync(CancellationToken.None));
        Assert.That(ownership.QuarantinedNonce, Is.EqualTo(transport.LaunchNonce));
        client.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Test]
    public async Task UnexpectedExit_FailsPendingWorkAndQuarantinesOwnership()
    {
        var transport = new ScriptedTransport();
        transport.OnSend = message => { var (id, method) = Request(message); if (method == "initialize") transport.Respond(id!.Value, new { }); };
        var ownership = new RecordingOwnership();
        await using var client = new CodexAppServerClient(transport, ownership, new SystemClock(), TimeSpan.FromSeconds(2), WorkspacePath);
        await client.InitializeAsync(CancellationToken.None);

        transport.Complete();
        await EventuallyAsync(() => ownership.QuarantinedNonce is not null);

        Assert.That(client.Events, Has.Some.Matches<CodexSessionEvent>(x => x.Kind == CodexSessionEventKinds.ProcessExited));
    }

    [Test]
    public async Task CancellationAndIdleTimeout_InterruptTurns()
    {
        var transport = new ScriptedTransport();
        transport.OnSend = message => RespondNormally(transport, message);
        await using var client = new CodexAppServerClient(transport, new RecordingOwnership(), new SystemClock(), TimeSpan.FromMilliseconds(40), WorkspacePath);
        await client.InitializeAsync(CancellationToken.None);
        var thread = await client.StartThreadAsync(CancellationToken.None);
        var first = await client.StartTurnAsync(thread.Id, "wait", CancellationToken.None);

        Assert.ThrowsAsync<CodexIdleTimeoutException>(async () => await client.WaitForTurnCompletionAsync(first, CancellationToken.None));

        var second = await client.StartTurnAsync(thread.Id, "wait", CancellationToken.None);
        using var cancellation = new CancellationTokenSource(20);
        Assert.ThrowsAsync<OperationCanceledException>(async () => await client.WaitForTurnCompletionAsync(second, cancellation.Token));
        Assert.That(transport.Methods.Count(x => x == "turn/interrupt"), Is.EqualTo(2));
    }

    [Test]
    public async Task FailedTurn_PreservesTerminalStatusAndIds()
    {
        var transport = new ScriptedTransport();
        transport.OnSend = message => RespondNormally(transport, message);
        await using var client = new CodexAppServerClient(transport, new RecordingOwnership(), new SystemClock(), TimeSpan.FromSeconds(2), WorkspacePath);
        await client.InitializeAsync(CancellationToken.None);
        var thread = await client.StartThreadAsync(CancellationToken.None);
        var failed = await client.StartTurnAsync(thread.Id, "fail", CancellationToken.None);
        transport.Emit("""{"method":"turn/completed","params":{"threadId":"thread-1","turn":{"id":"turn-1","status":"failed","error":{"message":"controlled failure"},"items":[]}}}""");

        var failure = await client.WaitForTurnCompletionAsync(failed, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(failure.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(failure.TurnId, Is.EqualTo("turn-1"));
            Assert.That(failure.Status, Is.EqualTo("failed"));
            Assert.That(failure.FailureReason, Is.EqualTo("controlled failure"));
            Assert.That(client.Events, Has.Some.Matches<CodexSessionEvent>(x => x.Kind == CodexSessionEventKinds.TurnFailed));
        });
    }

    [Test]
    public async Task InterruptedTurn_IsNormalizedAsTerminalInterruption()
    {
        var transport = new ScriptedTransport();
        transport.OnSend = message => RespondNormally(transport, message);
        await using var client = new CodexAppServerClient(transport, new RecordingOwnership(), new SystemClock(), TimeSpan.FromSeconds(2), WorkspacePath);
        await client.InitializeAsync(CancellationToken.None);
        var thread = await client.StartThreadAsync(CancellationToken.None);
        var turn = await client.StartTurnAsync(thread.Id, "interrupt", CancellationToken.None);
        transport.Emit("""{"method":"turn/completed","params":{"threadId":"thread-1","turn":{"id":"turn-1","status":"interrupted","items":[]}}}""");

        var result = await client.WaitForTurnCompletionAsync(turn, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo("interrupted"));
            Assert.That(client.Events, Has.Some.Matches<CodexSessionEvent>(x => x.Kind == CodexSessionEventKinds.TurnInterrupted));
        });
    }

    [Test]
    public async Task ImmediateTurnCompletion_IsRetainedUntilTurnRegistration()
    {
        var transport = new ScriptedTransport();
        transport.OnSend = message =>
        {
            var (id, method) = Request(message);
            if (method == "initialize") transport.Respond(id!.Value, new { });
            else if (method == "thread/start") transport.Respond(id!.Value, new { thread = new { id = "thread-1" }, cwd = WorkspacePath });
            else if (method == "turn/start")
            {
                transport.Respond(id!.Value, new { turn = new { id = "turn-early", status = "inProgress", items = Array.Empty<object>() } });
                transport.Emit("""{"method":"turn/completed","params":{"threadId":"thread-1","turn":{"id":"turn-early","status":"completed","items":[]}}}""");
            }
        };
        await using var client = new CodexAppServerClient(transport, new RecordingOwnership(), new SystemClock(), TimeSpan.FromMilliseconds(100), WorkspacePath);
        await client.InitializeAsync(CancellationToken.None);
        var thread = await client.StartThreadAsync(CancellationToken.None);

        var turn = await client.StartTurnAsync(thread.Id, "complete immediately", CancellationToken.None);
        var result = await client.WaitForTurnCompletionAsync(turn, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new CodexTurnResult("thread-1", "turn-early", "completed")));
            Assert.That(client.BufferedEarlyTurnResultCount, Is.Zero);
        });
    }

    [Test]
    public async Task SendFailure_RemovesPendingRequestCorrelation()
    {
        var transport = new ScriptedTransport();
        transport.OnSend = message => RespondNormally(transport, message);
        await using var client = new CodexAppServerClient(transport, new RecordingOwnership(), new SystemClock(), TimeSpan.FromSeconds(2), WorkspacePath);
        await client.InitializeAsync(CancellationToken.None);
        transport.SendFailure = new IOException("controlled send failure");

        Assert.ThrowsAsync<IOException>(async () => await client.StartThreadAsync(CancellationToken.None));
        Assert.That(client.PendingRequestCount, Is.Zero);
    }

    [Test]
    public async Task Dispose_StopsProcessAndReleasesMatchingOwnership()
    {
        var transport = new ScriptedTransport();
        transport.OnSend = message => { var (id, method) = Request(message); if (method == "initialize") transport.Respond(id!.Value, new { }); };
        var ownership = new RecordingOwnership();
        var client = new CodexAppServerClient(transport, ownership, new SystemClock(), TimeSpan.FromSeconds(2), WorkspacePath);
        await client.InitializeAsync(CancellationToken.None);

        await client.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(transport.StopCalled, Is.True);
            Assert.That(ownership.ReleasedNonce, Is.EqualTo(transport.LaunchNonce));
        });
    }

    [Test]
    public void ProcessConfiguration_UsesArgumentListParsingAndMinimalEnvironment()
    {
        var parsed = ProcessCodexJsonRpcTransport.ParseCommand("\"C:\\Program Files\\Codex\\codex.exe\" app-server");
        var environment = ProcessCodexJsonRpcTransport.BuildEnvironment(
            new Dictionary<string, string> { ["PATH"] = "tools", ["GITHUB_TOKEN"] = "secret", ["AUTOMATON_DB"] = "secret" },
            new Dictionary<string, string> { ["CODEX_STAGE4"] = "enabled" });

        Assert.Multiple(() =>
        {
            Assert.That(parsed.FileName, Is.EqualTo("C:\\Program Files\\Codex\\codex.exe"));
            Assert.That(parsed.Arguments, Is.EqualTo(new[] { "app-server" }));
            Assert.That(environment["PATH"], Is.EqualTo("tools"));
            Assert.That(environment["CODEX_STAGE4"], Is.EqualTo("enabled"));
            Assert.That(environment.ContainsKey("GITHUB_TOKEN"), Is.False);
            Assert.That(environment.ContainsKey("AUTOMATON_DB"), Is.False);
            Assert.That(() => ProcessCodexJsonRpcTransport.BuildEnvironment(new Dictionary<string, string>(), new Dictionary<string, string> { ["GITHUB_TOKEN"] = "secret" }), Throws.TypeOf<CodexProtocolException>());
        });
    }

    [Test]
    public async Task WorkspaceOwnershipLease_AcquiresAndReleasesTheSameLaunchNonce()
    {
        var workspace = new Workspace("key", Path.GetTempPath(), "branch", WorkspaceLifecycleStates.Ready, WorkspaceExecutionOwnershipStates.None, "reserved", "repo");
        var manager = new RecordingWorkspaceManager();
        var lease = new WorkspaceCodexExecutionOwnership(manager, workspace);

        await lease.AcquireAsync("run-nonce", CancellationToken.None);
        await lease.ReleaseAsync("run-nonce", CancellationToken.None);

        Assert.That(manager.Transitions, Is.EqualTo(new[]
        {
            (WorkspaceExecutionOwnershipStates.Active, "run-nonce"),
            (WorkspaceExecutionOwnershipStates.None, "run-nonce")
        }));
    }

    private static void RespondNormally(ScriptedTransport transport, string message)
    {
        var (id, method) = Request(message);
        switch (method)
        {
            case "initialize": transport.Respond(id!.Value, new { }); break;
            case "thread/start": transport.Respond(id!.Value, new { thread = new { id = "thread-1" }, cwd = WorkspacePath }); break;
            case "thread/resume": transport.Respond(id!.Value, new { thread = new { id = "thread-1" }, cwd = WorkspacePath }); break;
            case "turn/start": transport.Respond(id!.Value, new { turn = new { id = "turn-1", status = "inProgress", items = Array.Empty<object>() } }); break;
            case "turn/interrupt": transport.Respond(id!.Value, new { }); break;
        }
    }

    private static (long? Id, string Method) Request(string message)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        return (root.TryGetProperty("id", out var id) ? id.GetInt64() : null, root.GetProperty("method").GetString()!);
    }

    private static (string Method, string? Cwd) ParseMessage(string message)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        var parameters = root.TryGetProperty("params", out var value) ? value : default;
        return (
            root.GetProperty("method").GetString()!,
            parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("cwd", out var cwd) ? cwd.GetString() : null);
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200; attempt++) { if (condition()) return; await Task.Delay(10); }
        Assert.Fail("Timed out waiting for expected state.");
    }

    private sealed class RecordingOwnership : ICodexExecutionOwnership
    {
        public string? AcquiredNonce { get; private set; }
        public string? ReleasedNonce { get; private set; }
        public string? QuarantinedNonce { get; private set; }
        public Task AcquireAsync(string launchNonce, CancellationToken cancellationToken) { AcquiredNonce = launchNonce; return Task.CompletedTask; }
        public Task ReleaseAsync(string launchNonce, CancellationToken cancellationToken) { ReleasedNonce = launchNonce; return Task.CompletedTask; }
        public Task QuarantineAsync(string launchNonce, CancellationToken cancellationToken) { QuarantinedNonce = launchNonce; return Task.CompletedTask; }
    }

    private sealed class RecordingWorkspaceManager : IWorkspaceManager
    {
        public List<(string Status, string Nonce)> Transitions { get; } = [];
        public Task<Workspace> CreateOrRecoverAsync(WorkspaceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Workspace> UpdateLifecycleAsync(Workspace workspace, string lifecycleStatus, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CleanupTerminalAsync(Workspace workspace, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Workspace> UpdateExecutionOwnershipAsync(Workspace workspace, string executionOwnershipStatus, string launchNonce, CancellationToken cancellationToken)
        {
            Transitions.Add((executionOwnershipStatus, launchNonce));
            return Task.FromResult(workspace with { ExecutionOwnershipStatus = executionOwnershipStatus, LaunchNonce = launchNonce });
        }
    }

    private sealed class ScriptedTransport : ICodexJsonRpcTransport
    {
        private readonly Channel<string> messages = Channel.CreateUnbounded<string>();
        public string LaunchNonce { get; } = "launch-nonce";
        public CodexProcessIdentity? Identity { get; private set; }
        public Action<string>? OnSend { get; set; }
        public Exception? SendFailure { get; set; }
        public List<string> Methods { get; } = [];
        public List<string> Messages { get; } = [];
        public bool StopCalled { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken) { Identity = new(42, DateTimeOffset.UtcNow, LaunchNonce); return Task.CompletedTask; }
        public Task SendAsync(string message, CancellationToken cancellationToken)
        {
            Methods.Add(Request(message).Method);
            Messages.Add(message);
            if (SendFailure is { } failure) { SendFailure = null; return Task.FromException(failure); }
            OnSend?.Invoke(message);
            return Task.CompletedTask;
        }
        public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) => messages.Reader.ReadAllAsync(cancellationToken);
        public Task StopAsync(CancellationToken cancellationToken) { StopCalled = true; messages.Writer.TryComplete(); return Task.CompletedTask; }
        public void Respond(long id, object result) => Emit(JsonSerializer.Serialize(new { id, result }));
        public void Emit(string message) => messages.Writer.TryWrite(message);
        public void Complete() => messages.Writer.TryComplete();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
