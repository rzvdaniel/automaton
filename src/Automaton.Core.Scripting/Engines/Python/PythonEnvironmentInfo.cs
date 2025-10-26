namespace Automaton.Core.Scripting.Engines.Python;

public class PythonEnvironmentInfo
{
 public string? PythonHome { get; set; }
 public string? PythonPaths { get; set; } // os.pathsep-joined list
 public string? PythonExe { get; set; }
 public int? PythonMajor { get; set; }
 public int? PythonMinor { get; set; }

 // Extended properties for richer autodetection
 public string? PythonVersion { get; set; }
 public string? PythonDllPath { get; set; }
 public string? Architecture { get; set; }
 public List<string> Distributions { get; set; } = new();
 public List<string> SitePackagesPaths { get; set; } = new();
 public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
 public bool IsAutoDetectionSuccessful { get; set; }
 public string? Notes { get; set; }
}
