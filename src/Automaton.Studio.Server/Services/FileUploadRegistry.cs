using Automaton.Core.Models;
using System.Collections.Concurrent;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Tracks pending file upload sessions. When the runner finishes writing all chunks,
/// it calls FileUploadComplete which resolves the TCS and unblocks the upload endpoint.
/// </summary>
public class FileUploadRegistry
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<FileUploadCompleteMessage>> _pending = new();

    public (string sessionId, Task<FileUploadCompleteMessage> completionTask) Register(TimeSpan timeout, CancellationToken ct)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<FileUploadCompleteMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[sessionId] = tcs;

        ct.Register(() => TryFail(sessionId, "Upload cancelled"));
        _ = Task.Delay(timeout).ContinueWith(_ => TryFail(sessionId, "File upload timed out"));

        return (sessionId, tcs.Task);
    }

    public bool TryComplete(FileUploadCompleteMessage message)
    {
        if (_pending.TryRemove(message.SessionId, out var tcs))
        {
            tcs.TrySetResult(message);
            return true;
        }
        return false;
    }

    public bool TryFail(FileUploadFailedMessage message)
    {
        if (_pending.TryRemove(message.SessionId, out var tcs))
        {
            tcs.TrySetException(new InvalidOperationException(message.Error));
            return true;
        }
        return false;
    }

    private void TryFail(string sessionId, string reason)
    {
        if (_pending.TryRemove(sessionId, out var tcs))
            tcs.TrySetException(new TimeoutException(reason));
    }
}
