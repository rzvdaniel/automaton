namespace Automaton.Runner.Console.Services;

internal sealed class RunnerTunnelReconnectState
{
    private readonly object syncRoot = new();
    private int activeTunnelRequests;
    private bool reconnectRequested;
    private bool gracePeriodElapsed;

    public bool TryBeginTunnelRequest()
    {
        lock (syncRoot)
        {
            if (reconnectRequested)
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

    public bool RequestReconnect()
    {
        lock (syncRoot)
        {
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
}
