namespace Automaton.Studio.Server.Models;

/// <summary>
/// Python configuration for a runner
/// </summary>
public class RunnerPythonConfig
{
    /// <summary>
    /// Path to Python DLL (e.g., C:\Python311\python311.dll or /usr/lib/libpython3.11.so)
    /// </summary>
    public string? PythonDllPath { get; set; }
    
    /// <summary>
    /// Additional Python paths for libraries and modules
    /// </summary>
    public List<string> PythonPaths { get; set; } = new();
    
    /// <summary>
    /// Enable automatic Python path detection
    /// </summary>
    public bool AutoDetectPythonPaths { get; set; } = true;
}
