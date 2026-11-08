using System.Net.WebSockets;

namespace Automaton.Core.Models;

/// <summary>Moves WebSocket messages to and from tunnel frames. Fragments keep their boundaries, so nothing is reassembled.</summary>
public static class TunnelWebSocketPump
{
    public const int FragmentBytes = 32 * 1024;

    /// <summary>Reads the socket until it closes, handing each fragment to <paramref name="onFrame"/>.</summary>
    public static async Task ReadAsync(
        WebSocket socket, string requestId, Func<TunnelWebSocketFrame, Task> onFrame, CancellationToken cancellationToken)
    {
        var buffer = new byte[FragmentBytes];
        while (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await onFrame(new TunnelWebSocketFrame
                {
                    RequestId = requestId,
                    Kind = TunnelWebSocketFrameKinds.Close,
                    CloseStatus = (int?)result.CloseStatus,
                    CloseDescription = result.CloseStatusDescription,
                    Data = []
                });
                return;
            }

            await onFrame(new TunnelWebSocketFrame
            {
                RequestId = requestId,
                Kind = result.MessageType == WebSocketMessageType.Text
                    ? TunnelWebSocketFrameKinds.Text
                    : TunnelWebSocketFrameKinds.Binary,
                Data = buffer.AsSpan(0, result.Count).ToArray(),
                EndOfMessage = result.EndOfMessage
            });
        }
    }

    /// <summary>Writes one frame to the socket. Returns false after a close frame.</summary>
    public static async Task<bool> WriteAsync(WebSocket socket, TunnelWebSocketFrame frame, CancellationToken cancellationToken)
    {
        if (frame.Kind == TunnelWebSocketFrameKinds.Close)
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseOutputAsync(
                    frame.CloseStatus is null or 1005 or 1006 ? WebSocketCloseStatus.NormalClosure : (WebSocketCloseStatus)frame.CloseStatus.Value,
                    frame.CloseDescription, cancellationToken);
            return false;
        }

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            await socket.SendAsync(
                frame.Data,
                frame.Kind == TunnelWebSocketFrameKinds.Text ? WebSocketMessageType.Text : WebSocketMessageType.Binary,
                frame.EndOfMessage,
                cancellationToken);
        return true;
    }
}
