namespace Automaton.Runner.Console.Models;

/// <summary>
/// Python configuration stored in runner.json
/// </summary>
public class RunnerPythonConfig
{
    public string? PythonDllPath { get; set; }
    public List<string> PythonPaths { get; set; } = new();
    public bool AutoDetectPythonPaths { get; set; } = true;
}
