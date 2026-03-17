using Automaton.Core.Models;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// Tracks pending file download sessions. Each session has a channel that the server
/// writes incoming FileChunkReady messages into and the download endpoint reads from.
/// </summary>
public class FileDownloadRegistry
{
    private readonly ConcurrentDictionary<string, Channel<FileChunkReadyMessage>> _sessions = new();

    public Channel<FileChunkReadyMessage> Register(string sessionId)
    {
        var channel = Channel.CreateBounded<FileChunkReadyMessage>(new BoundedChannelOptions(4)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        _sessions[sessionId] = channel;
        return channel;
    }

    public bool TryWrite(FileChunkReadyMessage message)
    {
        if (_sessions.TryGetValue(message.SessionId, out var channel))
        {
            channel.Writer.TryWrite(message);
            return true;
        }
        return false;
    }

    public void Complete(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var channel))
            channel.Writer.TryComplete();
    }
}
