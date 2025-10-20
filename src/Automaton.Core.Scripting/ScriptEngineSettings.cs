namespace Automaton.Core.Scripting;

/// <summary>
/// Root container for all script engine settings
/// Supports multiple script engines through nested configuration
/// </summary>
public class RunnerScriptEngineSettings
{
    /// <summary>
    /// Python engine specific settings
    /// </summary>
    public PythonEngineSettings? Python { get; set; }

    // Future extensibility for additional script engines:
    // public NodeJsEngineSettings? NodeJs { get; set; }
    // public BashEngineSettings? Bash { get; set; }
    // public PowerShellEngineSettings? PowerShell { get; set; }
}

/// <summary>
/// Python-specific engine configuration
/// Used for both Python.NET and IronPython engines
/// </summary>
public class PythonEngineSettings
{
    /// <summary>
    /// Path to Python DLL (e.g., C:\Python312\python312.dll)
    /// If null or empty, auto-detection will be used
    /// </summary>
    public string? PythonDllPath { get; set; }

    /// <summary>
    /// List of paths to search for Python packages
    /// These paths are added to sys.path before script execution
    /// If empty, auto-detection will be used
    /// </summary>
    public List<string> PythonPaths { get; set; } = new();

    // Future Python-specific settings:
    // public bool? EnableVirtualEnv { get; set; }
    // public string? VirtualEnvPath { get; set; }
    // public List<string>? AllowedPackages { get; set; }
    // public Dictionary<string, string>? EnvironmentVariables { get; set; }
}
