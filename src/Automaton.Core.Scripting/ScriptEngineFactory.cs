namespace Automaton.Core.Scripting
{
    public class ScriptEngineFactory
    {
        private const string PythonScrypt = @"text/x-python";
        private const string BatchScript = @"text/x-batch";

        private readonly Dictionary<string, IScriptEngine> engines;
        private readonly ScriptEngineConfiguration configuration;

        public EventRaisingStreamWriter EventRaisingStreamWriter { get; set; }

        public ScriptEngineFactory(ScriptEngineConfiguration configuration)
        {
            this.configuration = configuration;
            EventRaisingStreamWriter = new EventRaisingStreamWriter();

            engines = new();

            InitializePythonEngine();
            InitializeBatchEngine();
        }

        private void InitializePythonEngine()
        {

            // Use Python.NET implementation with fallback
            try
            {
                engines[PythonScrypt] = new PythonNetScriptEngine(configuration);
            }
            catch (Exception ex)
            {
                throw;
            }

        }

        private void InitializeBatchEngine()
        {
            engines[BatchScript] = new BatchScriptEngine();
        }

        public IScriptEngine GetEngine(string contentType) => engines[contentType];
    }

    public class ScriptEngineConfiguration
    {
        public bool AutoDetectPythonPaths { get; set; } = true;
        public List<string> PythonPaths { get; set; } = new List<string>();
        public int DefaultTimeoutSeconds { get; set; } = 60; // Default timeout for script execution
        public int NetworkTimeoutSeconds { get; set; } = 30; // Timeout for network requests

        /// <summary>
        /// Load configuration from runner-specific settings
        /// User-specified paths take priority over auto-detection
        /// </summary>
        public void LoadFromRunnerSettings(RunnerScriptEngineSettings? settings)
        {
            if (settings?.Python == null) return;

            if (settings.Python.PythonPaths?.Any() == true)
            {
                PythonPaths = new List<string>(settings.Python.PythonPaths);
            }
        }

        /// <summary>
        /// Load configuration from desktop-specific settings
        /// Same logic as LoadFromRunnerSettings but kept separate for clarity
        /// </summary>
        public void LoadFromDesktopSettings(PythonEngineSettings? settings)
        {
            if (settings == null) return;

            if (settings.PythonPaths?.Any() == true)
            {
                PythonPaths = new List<string>(settings.PythonPaths);
            }
        }

        public void AddPythonPath(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && !PythonPaths.Contains(path))
            {
                PythonPaths.Add(path);
            }
        }
    }
}