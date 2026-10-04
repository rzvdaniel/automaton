namespace Automaton.Runner.Console.Services;

internal sealed class RunnerTunnelReconnectState
{
    private readonly object syncRoot = new();
    private int activeTunnelRequests;
    private bool reconnectRequested;
    private bool gracePeriodElapsed;
    private string? requestedTargetApiColor;
    private string? connectedApiColor;

    public bool TryBeginTunnelRequest()
    {
        lock (syncRoot)
        {
            if (reconnectRequested || IsWaitingForTargetApiColor())
                return false;

            activeTunnelRequests++;
            return true;
        }
    }

    public bool CompleteTunnelRequest()
    {
        lock (syncRoot)
        {
            activeTunnelRequests = Math.Max(0, activeTunnelRequests - 1);
            return reconnectRequested && activeTunnelRequests == 0;
        }
    }

    public bool RequestReconnect(string? targetApiColor = null)
    {
        lock (syncRoot)
        {
            if (!string.IsNullOrWhiteSpace(targetApiColor))
                requestedTargetApiColor = targetApiColor;

            if (IsConnectedToRequestedTarget())
            {
                requestedTargetApiColor = null;
                reconnectRequested = false;
                gracePeriodElapsed = false;
                return false;
            }

            reconnectRequested = true;
            return activeTunnelRequests == 0;
        }
    }

    public bool SetConnectedApiColor(string? apiColor)
    {
        lock (syncRoot)
        {
            connectedApiColor = apiColor;
            if (requestedTargetApiColor is null)
                return false;

            if (IsConnectedToRequestedTarget())
            {
                requestedTargetApiColor = null;
                reconnectRequested = false;
                gracePeriodElapsed = false;
                return false;
            }

            reconnectRequested = true;
            return activeTunnelRequests == 0;
        }
    }

    public bool ExpireGracePeriod()
    {
        lock (syncRoot)
        {
            if (!reconnectRequested)
                return false;

            gracePeriodElapsed = true;
            return true;
        }
    }

    public bool ShouldReconnect
    {
        get
        {
            lock (syncRoot)
                return reconnectRequested && (activeTunnelRequests == 0 || gracePeriodElapsed);
        }
    }

    public int ActiveTunnelRequestCount
    {
        get
        {
            lock (syncRoot)
                return activeTunnelRequests;
        }
    }

    public void Reset()
    {
        lock (syncRoot)
        {
            reconnectRequested = false;
            gracePeriodElapsed = false;
        }
    }

    private bool IsWaitingForTargetApiColor()
        => requestedTargetApiColor is not null
            && !string.Equals(connectedApiColor, requestedTargetApiColor, StringComparison.Ordinal);

    private bool IsConnectedToRequestedTarget()
        => requestedTargetApiColor is not null
            && string.Equals(connectedApiColor, requestedTargetApiColor, StringComparison.Ordinal);
}
