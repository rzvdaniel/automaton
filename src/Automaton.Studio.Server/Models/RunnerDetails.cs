using Automaton.Core.Enums;

namespace Automaton.Studio.Server.Models;

public class RunnerDetails
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string? ConnectionId { get; set; }
    public RunnerStatus Status { get; set; }
    
    // Python Configuration (parsed from Configuration JSON)
    public string? PythonDllPath { get; set; }
    public List<string> PythonPaths { get; set; } = new();
    public bool AutoDetectPythonPaths { get; set; } = true;
}
