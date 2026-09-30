namespace Automaton.Api.Hubs
{
    public static class AutomatonHubMethods
    {
        public const string JoinAppRuntimeLogs = "JoinAppRuntimeLogs";
        public const string LeaveAppRuntimeLogs = "LeaveAppRuntimeLogs";
        public const string BackendRuntimeLogEvent = "BackendRuntimeLogEvent";
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
        public const string DeployAppPackage = "DeployAppPackage";
        public const string ExecuteAppActionModule = "ExecuteAppActionModule";
        public const string CancelAppModuleExecution = "CancelAppModuleExecution";
        public const string ReadAppModuleExecutionLogs = "ReadAppModuleExecutionLogs";
        public const string ReadAppModuleExecutionResult = "ReadAppModuleExecutionResult";
        public const string ListAppActionModules = "ListAppActionModules";
        public const string ListAppActionVersions = "ListAppActionVersions";
        public const string RestoreAppActionVersion = "RestoreAppActionVersion";
        public const string DeleteAppActionVersion = "DeleteAppActionVersion";
        public const string PrepareAppActionModule = "PrepareAppActionModule";
        public const string SynchronizeAppActionModules = "SynchronizeAppActionModules";
        public const string CleanupAppDeploymentTarget = "CleanupAppDeploymentTarget";
        public const string SynchronizeApplicationManagedArtifacts = "SynchronizeApplicationManagedArtifacts";
    }
}
