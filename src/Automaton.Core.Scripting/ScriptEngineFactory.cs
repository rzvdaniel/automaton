using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using System.Text;

namespace Automaton.Core.Scripting
{
    public class ScriptEngineFactory
    {
        private const string PythonScrypt = @"text/x-python";
        private const string BatchScript = @"text/x-batch";

        private readonly Dictionary<string, ScriptRuntime> runtimes;
        private readonly Dictionary<string, IScriptEngine> engines;
        private readonly ScriptEngineConfiguration configuration;

        public EventRaisingStreamWriter EventRaisingStreamWriter { get; set; }

        public ScriptEngineFactory(ScriptEngineConfiguration configuration)
        {
            this.configuration = configuration;
            EventRaisingStreamWriter = new EventRaisingStreamWriter();

            runtimes = new();
            engines = new();

            InitializePythonEngine();
            InitializeBatchEngine();
        }

        private void InitializePythonEngine()
        {
            if (configuration.UsePythonNet)
            {
                // Use Python.NET implementation with fallback
                try
                {
                    engines[PythonScrypt] = new PythonNetScriptEngine(configuration);
                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine("✓ Python.NET engine initialized successfully");
                    }
                }
                catch (Exception ex)
                {
                    // Fallback to IronPython if Python.NET fails to initialize
                    if (configuration.EnableDebugMode)
                    {
                        Console.WriteLine($"Warning: Python.NET initialization failed ({ex.Message}), falling back to IronPython");
                    }
                    InitializeIronPython();
                }
            }
            else
            {
                // Use IronPython implementation (original)
                InitializeIronPython();
            }
        }

        private void InitializeIronPython()
        {
            runtimes[PythonScrypt] = IronPython.Hosting.Python.CreateRuntime();
            
            // Workaround for IronPython output
            runtimes[PythonScrypt].IO.SetOutput(EventRaisingStreamWriter, Encoding.ASCII);
            
            engines[PythonScrypt] = new PythonScriptEngine(IronPython.Hosting.Python.GetEngine(runtimes[PythonScrypt]));
        }

        private void InitializeBatchEngine()
        {
            engines[BatchScript] = new BatchScriptEngine();
        }

        public IScriptEngine GetEngine(string contentType) => engines[contentType];

        public string GetPythonEngineType() 
        {
            if (configuration.UsePythonNet && engines[PythonScrypt] is PythonNetScriptEngine)
            {
                return "Python.NET";
            }
            return "IronPython";
        }
    }

    public class ScriptEngineConfiguration
    {
        public bool UsePythonNet { get; set; } = false; // Default to IronPython for backward compatibility
        public bool EnableDebugMode { get; set; } = false;
        public bool AutoDetectPythonPaths { get; set; } = true;
        public List<string> PythonPaths { get; set; } = new List<string>();
        public string? PythonDllPath { get; set; } = null; // Path to Python DLL for Python.NET
        public int DefaultTimeoutSeconds { get; set; } = 60; // Default timeout for script execution
        public int NetworkTimeoutSeconds { get; set; } = 30; // Timeout for network requests

        /// <summary>
        /// Load configuration from runner-specific settings
        /// User-specified paths take priority over auto-detection
        /// </summary>
        public void LoadFromRunnerSettings(RunnerScriptEngineSettings? settings)
        {
            if (settings?.Python == null) return;

            if (!string.IsNullOrWhiteSpace(settings.Python.PythonDllPath))
            {
                PythonDllPath = settings.Python.PythonDllPath;
                if (EnableDebugMode)
                {
                    Console.WriteLine($"Using user-specified Python DLL: {PythonDllPath}");
                }
            }

            if (settings.Python.PythonPaths?.Any() == true)
            {
                PythonPaths = new List<string>(settings.Python.PythonPaths);
                if (EnableDebugMode)
                {
                    Console.WriteLine($"Using {PythonPaths.Count} user-specified Python paths");
                }
            }
        }

        /// <summary>
        /// Load configuration from desktop-specific settings
        /// Same logic as LoadFromRunnerSettings but kept separate for clarity
        /// </summary>
        public void LoadFromDesktopSettings(PythonEngineSettings? settings)
        {
            if (settings == null) return;

            if (!string.IsNullOrWhiteSpace(settings.PythonDllPath))
            {
                PythonDllPath = settings.PythonDllPath;
                if (EnableDebugMode)
                {
                    Console.WriteLine($"Using user-specified Python DLL: {PythonDllPath}");
                }
            }

            if (settings.PythonPaths?.Any() == true)
            {
                PythonPaths = new List<string>(settings.PythonPaths);
                if (EnableDebugMode)
                {
                    Console.WriteLine($"Using {PythonPaths.Count} user-specified Python paths");
                }
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