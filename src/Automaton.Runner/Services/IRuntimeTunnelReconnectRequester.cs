namespace Automaton.Runner.Console.Services;

public interface IRuntimeTunnelReconnectRequester
{
    void RequestRuntimeTunnelReconnect(string? targetApiInstanceId, string? targetApiColor, string? reason);
}
