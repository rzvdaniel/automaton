namespace Automaton.Studio.Server.Config;

public class SignalRConfig
{
    public int ClientTimeoutIntervalMinutes { get; set; } = 5;
    public int KeepAliveIntervalSeconds { get; set; } = 15;
    public int HandshakeTimeoutSeconds { get; set; } = 30;
    public long MaximumReceiveMessageSizeBytes { get; set; } = 1 * 1024 * 1024;
}
