using Automaton.Core.Models;
using Automaton.Steps;

namespace Automaton.Studio.Server.Models;

public class CustomStepDefinition
{
    public string? Code { get; set; }

    public CustomStepLanguage Language { get; set; } = CustomStepLanguage.Python;

    public IList<StepVariable>? CodeInputVariables { get; set; } = new List<StepVariable>();

    public IList<StepVariable>? CodeOutputVariables { get; set; } = new List<StepVariable>();
}
