namespace Automaton.Api.Hubs
{
    public static class AutomatonHubMethods
    {
        public const string JoinAppRuntimeLogs = "JoinAppRuntimeLogs";
        public const string LeaveAppRuntimeLogs = "LeaveAppRuntimeLogs";
        public const string BackendRuntimeLogEvent = "BackendRuntimeLogEvent";
        public const string RunWorkflow = "RunWorkflow";
        public const string RunCustomStep = "RunCustomStep";
        public const string RunInfrastructureCommand = "RunInfrastructureCommand";
        public const string UploadControllerScript = "UploadControllerScript";
        public const string TunnelRequest = "TunnelRequest";
        public const string TunnelResponse = "TunnelResponse";
        public const string Ping = "Ping";
        public const string ReceiveFileChunk = "ReceiveFileChunk";
        public const string FileUploadComplete = "FileUploadComplete";
        public const string FileUploadFailed = "FileUploadFailed";
        public const string FetchFileChunk = "FetchFileChunk";
        public const string FileChunkReady = "FileChunkReady";
        public const string GetAppMediaStats = "GetAppMediaStats";
        public const string GetAppMediaFileMetadata = "GetAppMediaFileMetadata";
        public const string ListAppBackendFiles = "ListAppBackendFiles";
        public const string ReadAppBackendFile = "ReadAppBackendFile";
        public const string DeleteAppBackendFile = "DeleteAppBackendFile";
        public const string DeployAppPackage = "DeployAppPackage";
    }
}
