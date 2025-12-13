using Automaton.Core.Models;
using Automaton.Studio.Enums;

namespace Automaton.Studio.Domain;

public class CustomStep
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = nameof(CustomStep);
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string MoreInfo { get; set; } = string.Empty;
    public bool VisibleInExplorer { get; set; }
    public string Icon { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }

    /// <summary>
    /// The WebApp ID this step is associated with (if linked to an app)
    /// </summary>
    public Guid? WebAppId { get; set; }

    /// <summary>
    /// The WebApp name this step is associated with (for display purposes)
    /// </summary>
    public string? WebAppName { get; set; }

    public CustomStepDefinition Definition { get; set; } = new CustomStepDefinition();
    public string Code => Definition.Code;
    public IList<StepVariable> InputVariables => Definition.CodeInputVariables;
    public IList<StepVariable> OutputVariables => Definition.CodeOutputVariables;
}
