using System.Collections.Concurrent;

namespace Automaton.Runner.Console.Services;

/// <summary>
/// Connects "result ready" messages from the Runner's result queue to the host-container operations waiting for them.
/// Matched by operation ID; a message nobody waits for (after a restart, a duplicate) is dropped.
/// </summary>
public sealed class HostContainerResultNotifier
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource> waiters = new();
    private readonly object gate = new();
    private TaskCompletionSource connected = NewSignal();

    /// <summary>False when the API offers no result queue (an older API): waiters then read the result on a fixed interval.</summary>
    public bool ResultQueueAvailable { get; set; } = true;

    public ResultWaiter Register(Guid operationId)
    {
        var signal = NewSignal();
        waiters[operationId] = signal;
        return new ResultWaiter(this, operationId, signal.Task);
    }

    /// <summary>Returns true when an operation was waiting for this ID.</summary>
    public bool Notify(Guid operationId)
        => waiters.TryGetValue(operationId, out var signal) && signal.TrySetResult();

    /// <summary>A task that completes the next time the result consumer (re)connects.</summary>
    public Task NextConnection()
    {
        lock (gate) return connected.Task;
    }

    public void ConsumerConnected()
    {
        TaskCompletionSource previous;
        lock (gate)
        {
            previous = connected;
            connected = NewSignal();
        }
        previous.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public sealed class ResultWaiter(HostContainerResultNotifier owner, Guid operationId, Task signaled) : IDisposable
    {
        public Task Signaled { get; } = signaled;
        public void Dispose() => owner.waiters.TryRemove(operationId, out _);
    }
}
