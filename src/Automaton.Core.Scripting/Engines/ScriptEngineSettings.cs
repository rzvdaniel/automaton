namespace Automaton.Core.Scripting.Engines;

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
    /// List of paths to search for Python packages
    /// These paths are added to sys.path before script execution
    /// If empty, auto-detection will be used
    /// </summary>
    public List<string> PythonPaths { get; set; } = new();

    /// <summary>
    /// List of autodetected Python installation/library paths
    /// </summary>
    public List<string> AutoDetectedPythonPaths { get; set; } = new();

    /// <summary>
    /// Detected Python version (e.g., "3.11.4")
    /// </summary>
    public string? PythonVersion { get; set; }

    /// <summary>
    /// Full path to the detected python executable (e.g., python.exe)
    /// </summary>
    public string? PythonExecutablePath { get; set; }

    /// <summary>
    /// Full path to the detected Python DLL (e.g., python311.dll)
    /// </summary>
    public string? PythonDllPath { get; set; }

    /// <summary>
    /// Relevant environment variables (e.g., PYTHONHOME, PYTHONPATH)
    /// </summary>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Indicates if autodetection was successful
    /// </summary>
    public bool? IsAutoDetectionSuccessful { get; set; }

    /// <summary>
    /// Names of detected Python distributions (e.g., "Anaconda", "CPython")
    /// </summary>
    public List<string>? DetectedDistributions { get; set; }

    /// <summary>
    /// Architecture of the detected Python (e.g., "x64", "x86")
    /// </summary>
    public string? DetectedArchitecture { get; set; }

    /// <summary>
    /// Paths to detected site-packages directories
    /// </summary>
    public List<string>? DetectedSitePackagesPaths { get; set; }

    /// <summary>
    /// Additional notes or warnings from the detection process
    /// </summary>
    public string? Notes { get; set; }

    // Future Python-specific settings:
    // public bool? EnableVirtualEnv { get; set; }
    // public string? VirtualEnvPath { get; set; }
    // public List<string>? AllowedPackages { get; set; }
}
