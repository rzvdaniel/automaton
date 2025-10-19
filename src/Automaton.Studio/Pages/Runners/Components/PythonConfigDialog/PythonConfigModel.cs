namespace Automaton.Studio.Pages.Runners.Components.PythonConfigDialog;

public class PythonConfigModel
{
    public Guid RunnerId { get; set; }
    public string RunnerName { get; set; } = string.Empty;
    public string? PythonDllPath { get; set; }
    public List<string> PythonPaths { get; set; } = new();
    public bool AutoDetectPythonPaths { get; set; } = true;
}
