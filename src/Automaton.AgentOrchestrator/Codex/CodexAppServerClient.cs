using System.Collections.Concurrent;
using System.Text.Json;
using Automaton.AgentOrchestrator.Domain;
using Automaton.AgentOrchestrator.Orchestration;

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
}

public interface ICodexJsonRpcTransport : IAsyncDisposable
{
    string LaunchNonce { get; }
    CodexProcessIdentity? Identity { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task SendAsync(string message, CancellationToken cancellationToken);
    IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}

public interface ICodexExecutionOwnership
{
    Task AcquireAsync(string launchNonce, CancellationToken cancellationToken);
    Task ReleaseAsync(string launchNonce, CancellationToken cancellationToken);
    Task QuarantineAsync(string launchNonce, CancellationToken cancellationToken);
}

public sealed class NoopCodexExecutionOwnership : ICodexExecutionOwnership
{
    public Task AcquireAsync(string launchNonce, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ReleaseAsync(string launchNonce, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task QuarantineAsync(string launchNonce, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class CodexAppServerClient(
    ICodexJsonRpcTransport transport,
    ICodexExecutionOwnership ownership,
    IClock clock,
    TimeSpan idleTimeout) : ICodexAppServerClient, IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = [];
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CodexTurnResult>> turns = [];
    private readonly List<CodexSessionEvent> events = [];
    private readonly CancellationTokenSource lifetime = new();
    private long nextRequestId;
    private Task? reader;
    private string? launchNonce;
    private int ownershipQuarantined;
    private DateTimeOffset lastActivityUtc;
    private bool stopping;

    public CodexProcessIdentity? ProcessIdentity => transport.Identity;
    public DateTimeOffset LastActivityUtc => lastActivityUtc;
    public IReadOnlyList<CodexSessionEvent> Events { get { lock (events) return events.ToArray(); } }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (reader is not null) throw new InvalidOperationException("Codex App Server is already initialized.");
        launchNonce = transport.LaunchNonce;
        await ownership.AcquireAsync(launchNonce, cancellationToken);
        try
        {
            await transport.StartAsync(cancellationToken);
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

    public async Task<CodexThread> StartThreadAsync(string cwd, CancellationToken cancellationToken)
    {
        var result = await RequestAsync(CodexAppServerMethods.ThreadStart, new { cwd }, cancellationToken);
        var id = RequiredNestedId(result, "thread");
        Touch(CodexSessionEventKinds.ThreadStarted, id);
        return new(id);
    }

    public async Task<CodexThread> ResumeThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        var result = await RequestAsync(CodexAppServerMethods.ThreadResume, new { threadId }, cancellationToken);
        var id = RequiredNestedId(result, "thread");
        Touch(CodexSessionEventKinds.ThreadResumed, id);
        return new(id);
    }

    public async Task<CodexTurn> StartTurnAsync(string threadId, string prompt, CancellationToken cancellationToken)
    {
        var input = new[] { new { type = "text", text = prompt, text_elements = Array.Empty<object>() } };
        var result = await RequestAsync(CodexAppServerMethods.TurnStart, new { threadId, input }, cancellationToken);
        var id = RequiredNestedId(result, "turn");
        turns.TryAdd(id, new(TaskCreationOptions.RunContinuationsAsynchronously));
        Touch(CodexSessionEventKinds.TurnStarted, threadId, id);
        return new(id, threadId);
    }

    public async Task<CodexTurnResult> WaitForTurnCompletionAsync(CodexTurn turn, CancellationToken cancellationToken)
    {
        if (!turns.TryGetValue(turn.Id, out var completion)) throw new CodexProtocolException("Unknown turn ID.");
        try
        {
            while (true)
            {
                var remaining = idleTimeout - (clock.UtcNow - lastActivityUtc);
                if (remaining <= TimeSpan.Zero) throw new CodexIdleTimeoutException();
                var delay = Task.Delay(remaining, cancellationToken);
                if (await Task.WhenAny(completion.Task, delay) == completion.Task) return await completion.Task;
                cancellationToken.ThrowIfCancellationRequested();
                if (clock.UtcNow - lastActivityUtc >= idleTimeout) throw new CodexIdleTimeoutException();
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
        finally { turns.TryRemove(turn.Id, out _); }
    }

    public async Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken)
        => _ = await RequestAsync(CodexAppServerMethods.TurnInterrupt, new { threadId, turnId }, cancellationToken);

    private async Task<JsonElement> RequestAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(id, completion)) throw new InvalidOperationException("JSON-RPC request correlation failed.");
        await SendAsync(new { id, method, @params = parameters }, cancellationToken);
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try { return await completion.Task; } finally { pending.TryRemove(id, out _); }
    }

    private Task NotifyAsync(string method, CancellationToken cancellationToken) => SendAsync(new { method }, cancellationToken);
    private Task SendAsync(object message, CancellationToken cancellationToken) => transport.SendAsync(JsonSerializer.Serialize(message), cancellationToken);

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var line in transport.ReadAllAsync(cancellationToken)) ProcessMessage(line);
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

    private void ProcessMessage(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        lastActivityUtc = clock.UtcNow;
        if (root.TryGetProperty("id", out var id) && id.TryGetInt64(out var requestId))
        {
            if (!pending.TryGetValue(requestId, out var completion)) return;
            if (root.TryGetProperty("error", out var error)) completion.TrySetException(new CodexProtocolException(Bounded(error.GetRawText())));
            else if (root.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
            else completion.TrySetException(new CodexProtocolException("JSON-RPC response has no result."));
            return;
        }
        if (!root.TryGetProperty("method", out var methodProperty)) throw new CodexProtocolException("JSON-RPC message has neither ID nor method.");
        var method = methodProperty.GetString();
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
    }

    private void CompleteTurn(JsonElement parameters, string? threadId, string? turnId)
    {
        if (threadId is null || turnId is null) throw new CodexProtocolException("turn/completed is missing thread or turn ID.");
        var status = NestedStringProperty(parameters, "turn", "status") ?? "failed";
        var failure = NestedStringProperty(parameters, "turn", "error", "message");
        var kind = status switch { "completed" => CodexSessionEventKinds.TurnCompleted, "interrupted" => CodexSessionEventKinds.TurnInterrupted, _ => CodexSessionEventKinds.TurnFailed };
        Touch(kind, threadId, turnId, failure);
        if (turns.TryGetValue(turnId, out var completion)) completion.TrySetResult(new(threadId, turnId, status, failure));
    }

    private async Task QuarantineAsync()
    {
        if (launchNonce is null || Interlocked.Exchange(ref ownershipQuarantined, 1) != 0) return;
        await ownership.QuarantineAsync(launchNonce, CancellationToken.None);
    }

    private static string RequiredNestedId(JsonElement result, string property)
        => NestedStringProperty(result, property, "id") ?? throw new CodexProtocolException($"{property} response has no ID.");
    private static string? StringProperty(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? NestedStringProperty(JsonElement element, params string[] path) { foreach (var name in path) { if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element)) return null; } return element.ValueKind == JsonValueKind.String ? element.GetString() : null; }
    private static long? FindLong(JsonElement element, string name) { if (element.ValueKind == JsonValueKind.Object) { foreach (var property in element.EnumerateObject()) { if (property.NameEquals(name) && property.Value.TryGetInt64(out var number)) return number; var nested = FindLong(property.Value, name); if (nested is not null) return nested; } } return null; }
    private static string Bounded(string value) => value.Length <= 512 ? value : value[..512];
    private void Touch(string kind, string? threadId = null, string? turnId = null, string? detail = null, long? inputTokens = null, long? outputTokens = null) { lastActivityUtc = clock.UtcNow; lock (events) { events.Add(new(kind, lastActivityUtc, threadId, turnId, Bounded(detail ?? string.Empty), inputTokens, outputTokens)); if (events.Count > 512) events.RemoveAt(0); } }
    private void FailAll(Exception exception) { foreach (var request in pending.Values) request.TrySetException(exception); foreach (var turn in turns.Values) turn.TrySetException(exception); }

    public async ValueTask DisposeAsync()
    {
        stopping = true;
        lifetime.Cancel();
        await transport.StopAsync(CancellationToken.None);
        if (reader is not null) await reader;
        if (launchNonce is not null && Volatile.Read(ref ownershipQuarantined) == 0) await ownership.ReleaseAsync(launchNonce, CancellationToken.None);
        await transport.DisposeAsync();
        lifetime.Dispose();
    }
}

public class CodexProtocolException(string message, Exception? inner = null) : Exception(message, inner);
public sealed class CodexProcessExitedException() : CodexProtocolException("Codex App Server exited unexpectedly.");
public sealed class CodexIdleTimeoutException() : TimeoutException("Codex App Server produced no meaningful activity before the idle timeout.");
