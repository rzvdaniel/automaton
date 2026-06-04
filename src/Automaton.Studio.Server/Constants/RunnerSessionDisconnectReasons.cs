namespace Automaton.Studio.Server.Constants;

public static class RunnerSessionDisconnectReasons
{
    public const string GracefulDisconnect = "graceful_disconnect";
    public const string SupersededConnection = "superseded_connection";
    public const string HeartbeatTimeout = "heartbeat_timeout";
}
