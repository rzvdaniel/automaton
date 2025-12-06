namespace Automaton.Studio.Models;

/// <summary>
/// Information about a Custom Step linked to an App (client-side)
/// </summary>
public class LinkedStepInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Language { get; set; } = "Python";
    public int Version { get; set; } = 1;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    
    // Variable info
    public bool HasInputVariables => InputCount > 0;
    public bool HasOutputVariables => OutputCount > 0;
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    public List<StepVariableInfo> InputVariables { get; set; } = new();
    public List<StepVariableInfo> OutputVariables { get; set; } = new();
}

public class StepVariableInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = "String";
}
