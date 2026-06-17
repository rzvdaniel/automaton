using Automaton.Core.Models;
using Automaton.Steps.Enums;

namespace Automaton.Api.Models;

public class CustomStepDefinition
{
    public string? Code { get; set; }

    public string Language { get; set; } = CustomStepLanguage.Python;

    public IList<StepVariable>? CodeInputVariables { get; set; } = [];

    public IList<StepVariable>? CodeOutputVariables { get; set; } = [];

    /// <summary>
    /// Version of this custom step definition. Incremented each time the step is saved.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// ID of the source custom step template this instance was created from.
    /// </summary>
    public Guid? SourceCustomStepId { get; set; }

    /// <summary>
    /// Version of the source custom step template when this instance was created.
    /// </summary>
    public int? SourceVersion { get; set; }
}
