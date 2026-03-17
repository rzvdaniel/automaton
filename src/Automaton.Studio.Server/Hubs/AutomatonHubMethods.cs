namespace Automaton.Studio.Server.Hubs
{
    public static class AutomatonHubMethods
    {
        public const string RunWorkflow = "RunWorkflow";
        public const string RunCustomStep = "RunCustomStep";
        public const string UploadControllerScript = "UploadControllerScript";
        public const string TunnelRequest = "TunnelRequest";
        public const string TunnelResponse = "TunnelResponse";
        public const string Ping = "Ping";
        public const string ReceiveFileChunk = "ReceiveFileChunk";
        public const string FileUploadComplete = "FileUploadComplete";
        public const string FileUploadFailed = "FileUploadFailed";
        public const string FetchFileChunk = "FetchFileChunk";
        public const string FileChunkReady = "FileChunkReady";
    }
}
