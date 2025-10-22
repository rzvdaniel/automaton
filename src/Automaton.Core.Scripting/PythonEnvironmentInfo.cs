namespace Automaton.Core.Scripting;

public class PythonEnvironmentInfo
{
 public string? PythonHome { get; set; }
 public string? PythonPaths { get; set; } // os.pathsep-joined list
 public string? PythonExe { get; set; }
 public int? PythonMajor { get; set; }
 public int? PythonMinor { get; set; }
}
