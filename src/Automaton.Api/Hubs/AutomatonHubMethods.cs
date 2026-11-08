namespace Automaton.Api.Hubs
{
    public static class AutomatonHubMethods
    {
        public const string JoinAppRuntimeLogs = "JoinAppRuntimeLogs";
        public const string LeaveAppRuntimeLogs = "LeaveAppRuntimeLogs";
        public const string BackendRuntimeLogEvent = "BackendRuntimeLogEvent";
        public const string TunnelRequest = "TunnelRequest";
        public const string TunnelResponse = "TunnelResponse";
        public const string TunnelResponseChunk = "TunnelResponseChunk";
        public const string TunnelAck = "TunnelAck";
        public const string TunnelCancel = "TunnelCancel";
        public const string TunnelWebSocketOpen = "TunnelWebSocketOpen";
        public const string TunnelWebSocketFrame = "TunnelWebSocketFrame";
    }
}
