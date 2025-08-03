using Automaton.Core.Models;

namespace Automaton.Studio.Domain;

public class CustomStepDefinition
{
    public string Code { get; set; }

    public string Language { get; set; } = CustomStepLanguage.Python;

    public IList<StepVariable> CodeInputVariables { get; set; } = [];

    public IList<StepVariable> CodeOutputVariables { get; set; } = [];
}
