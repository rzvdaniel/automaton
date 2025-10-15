namespace Automaton.Studio.Server.Hubs
{
    public static class AutomatonHubMethods
    {
        public const string RunWorkflow = "RunWorkflow";
        public const string RunCustomStep = "RunCustomStep";
        public const string Ping = "Ping";
        
        // Python Configuration Methods
        public const string UpdatePythonConfig = "UpdatePythonConfig";
        public const string ValidatePythonPaths = "ValidatePythonPaths";
        public const string PythonConfigUpdated = "PythonConfigUpdated";
        public const string PythonPathsValidated = "PythonPathsValidated";
    }
}
