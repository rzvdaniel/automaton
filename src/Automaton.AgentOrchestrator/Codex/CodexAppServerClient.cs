using System.Collections.Concurrent;
using System.Text.Json;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;
using Automaton.AgentOrchestrator.Workflows;

namespace Automaton.AgentOrchestrator.Codex;

public static class CodexAppServerMethods
{
    public const string Initialize = "initialize";
    public const string Initialized = "initialized";
    public const string ThreadStart = "thread/start";
    public const string ThreadResume = "thread/resume";
    public const string TurnStart = "turn/start";
    public const string TurnInterrupt = "turn/interrupt";
    public const string TurnStarted = "turn/started";
    public const string TurnCompleted = "turn/completed";
    public const string ItemStarted = "item/started";
    public const string ItemCompleted = "item/completed";
    public const string TokenUsageUpdated = "thread/tokenUsage/updated";
    public const string CommandApproval = "item/commandExecution/requestApproval";
    public const string FileApproval = "item/fileChange/requestApproval";
    public const string UserInput = "item/tool/requestUserInput";
    public const string PermissionsApproval = "item/permissions/requestApproval";
}

public interface ICodexJsonRpcTransport : IAsyncDisposable
{
    string LaunchNonce { get; }
    CodexProcessIdentity? Identity { get; }
    DurableExecutionIdentity? DurableIdentity { get; }
    bool? IsProcessAlive => null;
    Task StartAsync(CancellationToken cancellationToken);
    Task SendAsync(string message, CancellationToken cancellationToken);
    IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface ICodexExecutionOwnership
{
    Task AcquireAsync(string launchNonce, CancellationToken cancellationToken);
    Task RecordProcessIdentityAsync(DurableExecutionIdentity identity, CancellationToken cancellationToken);
    Task ReleaseAsync(string launchNonce, CancellationToken cancellationToken);
    Task QuarantineAsync(string launchNonce, CancellationToken cancellationToken);
}

public sealed class NoopCodexExecutionOwnership : ICodexExecutionOwnership
{
    public Task AcquireAsync(string launchNonce, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task RecordProcessIdentityAsync(DurableExecutionIdentity identity, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ReleaseAsync(string launchNonce, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task QuarantineAsync(string launchNonce, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class CodexAppServerClient(
    ICodexJsonRpcTransport transport,
    ICodexExecutionOwnership ownership,
    IClock clock,
    TimeSpan turnTimeout,
    TimeSpan stallTimeout,
    string workspacePath,
    AgentExecutionProfile? profile = null) : ICodexAppServerClient, IAsyncDisposable
{
    public CodexAppServerClient(ICodexJsonRpcTransport transport, ICodexExecutionOwnership ownership, IClock clock, TimeSpan timeout, string workspacePath)
        : this(transport, ownership, clock, timeout, timeout, workspacePath) { }
    private const int MaximumEarlyTurnResults = 64;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = [];
    private readonly object turnGate = new();
    private readonly Dictionary<string, TaskCompletionSource<CodexTurnResult>> turns = [];
    private readonly Dictionary<string, DateTimeOffset> turnStartedUtc = [];
    private readonly Dictionary<string, (CodexTurnResult Result, LinkedListNode<string> Node)> earlyTurnResults = [];
    private readonly LinkedList<string> earlyTurnOrder = [];
    private readonly List<CodexSessionEvent> events = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly string workspacePath = NormalizeAbsolutePath(workspacePath, "Assigned workspace path");
    private long nextRequestId;
    private Task? reader;
    private string? launchNonce;
    private int ownershipQuarantined;
    private DateTimeOffset lastActivityUtc;
    private bool stopping;

    public CodexProcessIdentity? ProcessIdentity => transport.Identity;
    public DateTimeOffset LastActivityUtc => lastActivityUtc;
    public string? LastEventType { get; private set; }
    public TimeSpan IdleTimeout => stallTimeout;
    public bool? IsProcessAlive => transport.IsProcessAlive;
    public IReadOnlyList<CodexSessionEvent> Events { get { lock (events) return events.ToArray(); } }
    internal int PendingRequestCount => pending.Count;
    internal int BufferedEarlyTurnResultCount { get { lock (turnGate) return earlyTurnResults.Count; } }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (reader is not null) throw new InvalidOperationException("Codex App Server is already initialized.");
        launchNonce = transport.LaunchNonce;
        await ownership.AcquireAsync(launchNonce, cancellationToken);
        try
        {
            await transport.StartAsync(cancellationToken);
            if (transport.DurableIdentity is not { } identity) throw new CodexProtocolException("Codex process identity was not established.");
            await ownership.RecordProcessIdentityAsync(identity, cancellationToken);
            Touch(CodexSessionEventKinds.ProcessStarted);
            reader = ReadAsync(lifetime.Token);
            var initializeResult = await RequestAsync(CodexAppServerMethods.Initialize, new { clientInfo = new { name = "automaton-agent-orchestrator", version = "stage-4" }, capabilities = new { experimentalApi = false } }, cancellationToken);
            if (initializeResult.ValueKind != JsonValueKind.Object) throw new CodexProtocolException("initialize response must be an object.");
            await NotifyAsync(CodexAppServerMethods.Initialized, cancellationToken);
            Touch(CodexSessionEventKinds.Initialized);
        }
        catch
        {
            await QuarantineAsync();
            throw;
        }
    }

    public async Task<CodexThread> StartThreadAsync(CancellationToken cancellationToken)
    {
        var result = await RequestAsync(CodexAppServerMethods.ThreadStart, new { cwd = workspacePath, sandbox = SandboxMode(profile ?? AgentExecutionProfile.DefaultDeveloper) }, cancellationToken);
        var id = RequiredNestedId(result, "thread");
        ValidateResponseWorkspace(result, "thread/start");
        Touch(CodexSessionEventKinds.ThreadStarted, id);
        return new(id);
    }

    public async Task<CodexThread> ResumeThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        var result = await RequestAsync(CodexAppServerMethods.ThreadResume, new { threadId, cwd = workspacePath }, cancellationToken);
        var id = RequiredNestedId(result, "thread");
        ValidateResponseWorkspace(result, "thread/resume");
        Touch(CodexSessionEventKinds.ThreadResumed, id);
        return new(id);
    }

    public async Task<CodexTurn> StartTurnAsync(string threadId, string prompt, CancellationToken cancellationToken)
    {
        var input = new[] { new { type = "text", text = prompt, text_elements = Array.Empty<object>() } };
        var selectedProfile = profile ?? AgentExecutionProfile.DefaultDeveloper;
        var result = await RequestAsync(CodexAppServerMethods.TurnStart, new { threadId, input, model = string.IsNullOrWhiteSpace(selectedProfile.Model) ? null : selectedProfile.Model, sandboxPolicy = SandboxPolicy(selectedProfile) }, cancellationToken);
        var id = RequiredNestedId(result, "turn");
        lock (turnGate)
        {
            var completion = new TaskCompletionSource<CodexTurnResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!turns.TryAdd(id, completion)) throw new CodexProtocolException("Duplicate turn ID.");
            turnStartedUtc[id] = clock.UtcNow;
            if (earlyTurnResults.Remove(id, out var earlyResult))
            {
                earlyTurnOrder.Remove(earlyResult.Node);
                completion.TrySetResult(earlyResult.Result);
            }
        }
        Touch(CodexSessionEventKinds.TurnStarted, threadId, id);
        return new(id, threadId);
    }

    private static string SandboxMode(AgentExecutionProfile profile)
        => profile.WorkspaceAccess == "read-only" ? "read-only" : "workspace-write";

    private static object SandboxPolicy(AgentExecutionProfile profile)
        => profile.WorkspaceAccess == "read-only"
            ? new { type = "readOnly", networkAccess = false }
            : new { type = "workspaceWrite", networkAccess = false, writableRoots = Array.Empty<string>() };

    public async Task<CodexTurnResult> WaitForTurnCompletionAsync(CodexTurn turn, CancellationToken cancellationToken)
    {
        TaskCompletionSource<CodexTurnResult> completion;
        lock (turnGate)
            if (!turns.TryGetValue(turn.Id, out completion!)) throw new CodexProtocolException("Unknown turn ID.");
        try
        {
            while (true)
            {
                DateTimeOffset started;
                lock (turnGate) started = turnStartedUtc[turn.Id];
                var now = clock.UtcNow;
                var totalRemaining = turnTimeout - (now - started);
                var stallRemaining = stallTimeout - (now - lastActivityUtc);
                if (stallRemaining <= TimeSpan.Zero) throw new CodexIdleTimeoutException(CreateTimeoutDiagnostic("stall", stallTimeout, started, now));
                if (totalRemaining <= TimeSpan.Zero) throw new CodexTurnTimeoutException(CreateTimeoutDiagnostic("turn", turnTimeout, started, now));
                var remaining = totalRemaining <= stallRemaining ? totalRemaining : stallRemaining;
                var delay = clock.DelayAsync(remaining, cancellationToken);
                if (await Task.WhenAny(completion.Task, delay) == completion.Task) return await completion.Task;
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                now = clock.UtcNow;
                if (now - lastActivityUtc >= stallTimeout) throw new CodexIdleTimeoutException(CreateTimeoutDiagnostic("stall", stallTimeout, started, now));
                if (now - started >= turnTimeout) throw new CodexTurnTimeoutException(CreateTimeoutDiagnostic("turn", turnTimeout, started, now));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await InterruptTurnAsync(turn.ThreadId, turn.Id, CancellationToken.None);
            throw;
        }
        catch (CodexIdleTimeoutException)
        {
            await InterruptTurnAsync(turn.ThreadId, turn.Id, CancellationToken.None);
            throw;
        }
        catch (CodexTurnTimeoutException)
        {
            await InterruptTurnAsync(turn.ThreadId, turn.Id, CancellationToken.None);
            throw;
        }
        finally { lock (turnGate) { turns.Remove(turn.Id); turnStartedUtc.Remove(turn.Id); } }
    }

    private CodexTimeoutDiagnostic CreateTimeoutDiagnostic(string kind, TimeSpan configuredTimeout, DateTimeOffset started, DateTimeOffset now)
        => new(kind, configuredTimeout, now - started, now - lastActivityUtc, lastActivityUtc, LastEventType, transport.IsProcessAlive);

    public async Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken)
        => _ = await RequestAsync(CodexAppServerMethods.TurnInterrupt, new { threadId, turnId }, cancellationToken);

    private async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(id, completion)) throw new InvalidOperationException("JSON-RPC request correlation failed.");
        try
        {
            await SendAsync(new { id, method, @params = parameters }, cancellationToken);
            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return await completion.Task;
        }
        finally { pending.TryRemove(id, out _); }
    }

    private Task NotifyAsync(string method, CancellationToken cancellationToken) => SendAsync(new { method }, cancellationToken);
    private Task SendAsync(object message, CancellationToken cancellationToken) => transport.SendAsync(JsonSerializer.Serialize(message), cancellationToken);

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var line in transport.ReadAllAsync(cancellationToken)) await ProcessMessageAsync(line);
            if (!stopping)
            {
                var exception = new CodexProcessExitedException();
                FailAll(exception);
                Touch(CodexSessionEventKinds.ProcessExited, detail: exception.Message);
                await QuarantineAsync();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            var protocol = exception as CodexProtocolException ?? new CodexProtocolException("Malformed App Server protocol message.", exception);
            FailAll(protocol);
            Touch(CodexSessionEventKinds.ProtocolFailure, detail: protocol.Message);
            await QuarantineAsync();
        }
    }

    private async Task ProcessMessageAsync(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        lastActivityUtc = clock.UtcNow;
        LastEventType = ClassifyEvent(root);
        if (root.TryGetProperty("method", out var methodProperty))
        {
            var method = methodProperty.GetString();
            LastEventType = method;
            var parameters = root.TryGetProperty("params", out var value) ? value : default;
            var threadId = StringProperty(parameters, "threadId");
            var turnId = StringProperty(parameters, "turnId") ?? NestedStringProperty(parameters, "turn", "id");
            switch (method)
            {
                case CodexAppServerMethods.TurnStarted: Touch(CodexSessionEventKinds.TurnStarted, threadId, turnId); break;
                case CodexAppServerMethods.ItemStarted:
                case CodexAppServerMethods.ItemCompleted: Touch(CodexSessionEventKinds.ItemActivity, threadId, turnId, method); break;
                case CodexAppServerMethods.TokenUsageUpdated:
                    Touch(CodexSessionEventKinds.UsageUpdated, threadId, turnId, inputTokens: FindLong(parameters, "inputTokens"), outputTokens: FindLong(parameters, "outputTokens")); break;
                case CodexAppServerMethods.TurnCompleted: CompleteTurn(parameters, threadId, turnId); break;
                default: Touch(CodexSessionEventKinds.ItemActivity, threadId, turnId, method); break;
            }
            if (root.TryGetProperty("id", out var serverRequestId))
                await HandleServerRequestAsync(serverRequestId, method, parameters);
            return;
        }
        if (!root.TryGetProperty("id", out var id) || !id.TryGetInt64(out var requestId)
            || (!root.TryGetProperty("result", out var result) && !root.TryGetProperty("error", out var error)))
            throw new CodexProtocolException("Malformed JSON-RPC message shape.");
        if (!pending.TryGetValue(requestId, out var completion))
            throw new CodexProtocolException("JSON-RPC response did not match a pending request.");
        if (root.TryGetProperty("error", out error)) completion.TrySetException(new CodexProtocolException(Bounded(error.GetRawText())));
        else completion.TrySetResult(result.Clone());
    }

    private async Task HandleServerRequestAsync(JsonElement requestId, string? method, JsonElement parameters)
    {
        object response;
        switch (method)
        {
            case CodexAppServerMethods.CommandApproval:
                response = new { decision = IsAssignedWorkspace(parameters) ? "accept" : "decline" };
                break;
            case CodexAppServerMethods.FileApproval:
                response = new { decision = IsFileApprovalSafe(parameters) ? "accept" : "decline" };
                break;
            case CodexAppServerMethods.UserInput:
                response = new { answers = new Dictionary<string, object>() };
                break;
            case CodexAppServerMethods.PermissionsApproval:
                response = new { permissions = new { fileSystem = (object?)null, network = (object?)null } };
                break;
            default:
                await SendRawAsync(new { jsonrpc = "2.0", id = requestId.Clone(), error = new { code = -32601, message = "Unsupported server request." } }, CancellationToken.None);
                return;
        }
        await SendRawAsync(new { jsonrpc = "2.0", id = requestId.Clone(), result = response }, CancellationToken.None);
    }

    private bool IsAssignedWorkspace(JsonElement parameters)
    {
        var cwd = StringProperty(parameters, "cwd") ?? StringProperty(parameters, "grantRoot");
        return cwd is not null && PathsEqual(cwd, workspacePath);
    }

    private bool IsFileApprovalSafe(JsonElement parameters)
    {
        var grantRoot = StringProperty(parameters, "grantRoot");
        return grantRoot is null || PathsEqual(grantRoot, workspacePath);
    }

    private Task SendRawAsync(object message, CancellationToken cancellationToken)
        => transport.SendAsync(JsonSerializer.Serialize(message), cancellationToken);

    private static string ClassifyEvent(JsonElement root)
    {
        if (!root.TryGetProperty("method", out var methodProperty)) return "response";
        return methodProperty.GetString() switch
        {
            CodexAppServerMethods.TurnStarted => CodexAppServerMethods.TurnStarted,
            CodexAppServerMethods.ItemStarted => CodexAppServerMethods.ItemStarted,
            CodexAppServerMethods.ItemCompleted => CodexAppServerMethods.ItemCompleted,
            CodexAppServerMethods.TokenUsageUpdated => CodexAppServerMethods.TokenUsageUpdated,
            CodexAppServerMethods.TurnCompleted => CodexAppServerMethods.TurnCompleted,
            _ => "unknown-notification"
        };
    }

    private void CompleteTurn(JsonElement parameters, string? threadId, string? turnId)
    {
        if (threadId is null || turnId is null) throw new CodexProtocolException("turn/completed is missing thread or turn ID.");
        var status = NestedStringProperty(parameters, "turn", "status") ?? "failed";
        var failure = NestedStringProperty(parameters, "turn", "error", "message");
        var kind = status switch { "completed" => CodexSessionEventKinds.TurnCompleted, "interrupted" => CodexSessionEventKinds.TurnInterrupted, _ => CodexSessionEventKinds.TurnFailed };
        Touch(kind, threadId, turnId, failure);
        DateTimeOffset started;
        lock (turnGate) started = turnStartedUtc.TryGetValue(turnId, out var value) ? value : clock.UtcNow;
        var finalResponse = ExtractFinalResponse(parameters);
        var diagnostic = status.Equals("completed", StringComparison.OrdinalIgnoreCase) ? null :
            new CodexTurnDiagnostic(status, FinalResponsePresent: !string.IsNullOrWhiteSpace(finalResponse), clock.UtcNow - started,
                clock.UtcNow - lastActivityUtc, LastEventType, transport.IsProcessAlive, TurnCompletedObserved: true, Termination: status switch
                { "failed" => "provider_failed", "interrupted" => "interrupted", _ => "unknown" });
        var result = new CodexTurnResult(threadId, turnId, status, failure, finalResponse, diagnostic);
        lock (turnGate)
        {
            if (turns.TryGetValue(turnId, out var completion)) completion.TrySetResult(result);
            else BufferEarlyTurnResult(result);
        }
    }

    private async Task QuarantineAsync()
    {
        if (launchNonce is null || Interlocked.Exchange(ref ownershipQuarantined, 1) != 0) return;
        await ownership.QuarantineAsync(launchNonce, CancellationToken.None);
    }

    private static string RequiredNestedId(JsonElement result, string property)
        => NestedStringProperty(result, property, "id") ?? throw new CodexProtocolException($"{property} response has no ID.");
    private void ValidateResponseWorkspace(JsonElement result, string method)
    {
        var returnedCwd = StringProperty(result, "cwd")
            ?? throw new CodexProtocolException($"{method} response has no workspace path.");
        if (!PathsEqual(returnedCwd, workspacePath))
            throw new CodexProtocolException($"{method} returned a different workspace path.");
    }
    private static bool PathsEqual(string left, string right)
        => string.Equals(
            NormalizeAbsolutePath(left, "App Server workspace path"),
            NormalizeAbsolutePath(right, "Assigned workspace path"),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static string NormalizeAbsolutePath(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new CodexProtocolException($"{description} must be absolute.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private void BufferEarlyTurnResult(CodexTurnResult result)
    {
        if (earlyTurnResults.ContainsKey(result.TurnId)) return;
        while (earlyTurnResults.Count >= MaximumEarlyTurnResults && earlyTurnOrder.First is { } oldest)
        {
            earlyTurnOrder.RemoveFirst();
            earlyTurnResults.Remove(oldest.Value);
        }
        var node = earlyTurnOrder.AddLast(result.TurnId);
        earlyTurnResults[result.TurnId] = (result, node);
    }
    private static string? StringProperty(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? ExtractFinalResponse(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("turn", out var turn)
            || !turn.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array) return null;
        string? response = null;
        foreach (var item in items.EnumerateArray())
            if (StringProperty(item, "type") == "agentMessage" && StringProperty(item, "text") is { } text)
                response = text;
        return response is null ? null : response.Length <= 16_384 ? response : response[..16_384];
    }
    private static string? NestedStringProperty(JsonElement element, params string[] path) { foreach (var name in path) { if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element)) return null; } return element.ValueKind == JsonValueKind.String ? element.GetString() : null; }
    private static long? FindLong(JsonElement element, string name) { if (element.ValueKind == JsonValueKind.Object) { foreach (var property in element.EnumerateObject()) { if (property.NameEquals(name) && property.Value.TryGetInt64(out var number)) return number; var nested = FindLong(property.Value, name); if (nested is not null) return nested; } } return null; }
    private static string Bounded(string value) => value.Length <= 512 ? value : value[..512];
    private void Touch(string kind, string? threadId = null, string? turnId = null, string? detail = null, long? inputTokens = null, long? outputTokens = null) { lastActivityUtc = clock.UtcNow; lock (events) { events.Add(new(kind, lastActivityUtc, threadId, turnId, Bounded(detail ?? string.Empty), inputTokens, outputTokens)); if (events.Count > 512) events.RemoveAt(0); } }
    private void FailAll(Exception exception) { foreach (var request in pending.Values) request.TrySetException(exception); lock (turnGate) foreach (var turn in turns.Values) turn.TrySetException(exception); }

    public async ValueTask DisposeAsync()
    {
        stopping = true;
        lifetime.Cancel();
        try { await transport.StopAsync(CancellationToken.None); }
        catch
        {
            await QuarantineAsync();
            throw;
        }
        if (reader is not null) await reader;
        if (launchNonce is not null && Volatile.Read(ref ownershipQuarantined) == 0) await ownership.ReleaseAsync(launchNonce, CancellationToken.None);
        await transport.DisposeAsync();
        lifetime.Dispose();
    }
}

public class CodexProtocolException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class CodexProcessExitedException() : CodexProtocolException("Codex App Server exited unexpectedly.");
public sealed record CodexTimeoutDiagnostic(
    string Kind,
    TimeSpan ConfiguredTimeout,
    TimeSpan TotalElapsed,
    TimeSpan InactivityDuration,
    DateTimeOffset LastActivityUtc,
    string? LastEventType,
    bool? ProcessAlive);
public sealed record CodexTurnDiagnostic(
    string Status,
    bool FinalResponsePresent,
    TimeSpan TotalElapsed,
    TimeSpan InactivityDuration,
    string? LastEventType,
    bool? ProcessAlive,
    bool TurnCompletedObserved,
    string Termination);
public sealed class CodexIdleTimeoutException(CodexTimeoutDiagnostic? diagnostic = null) : TimeoutException("Codex App Server produced no protocol activity before the stall timeout.")
{
    public CodexTimeoutDiagnostic? Diagnostic { get; } = diagnostic;
}
public sealed class CodexTurnTimeoutException(CodexTimeoutDiagnostic? diagnostic = null) : TimeoutException("Codex App Server turn exceeded the configured total timeout.")
{
    public CodexTimeoutDiagnostic? Diagnostic { get; } = diagnostic;
}
