using System.Text.Json.Serialization;

namespace Automaton.Core.Models;

public class Step
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? MoreInfo { get; set; }

    public string? Category { get; set; }

    public bool VisibleInExplorer { get; set; }

    public string Icon { get; set; } = string.Empty;

    public IDictionary<string, StepVariable> Inputs { get; set; } = new Dictionary<string, StepVariable>();

    public IDictionary<string, StepVariable> Outputs { get; set; } = new Dictionary<string, StepVariable>();

    public string? NextStepId { get; set; }

    public string? ParentId { get; set; }

    /// <summary>
    /// For custom steps - stores the complete definition including version tracking
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? StepDefinition { get; set; }
}
