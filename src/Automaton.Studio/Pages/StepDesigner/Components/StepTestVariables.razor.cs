using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Studio.Steps.Custom.Variables;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.StepDesigner.Components;

public partial class StepTestVariables : ComponentBase
{
    [Parameter] public IList<StepVariable>? InputVariables { get; set; }
    [Parameter] public IList<StepVariable>? OutputVariables { get; set; }

    private Type GetVariableComponent(VariableType type)
    {
        return type switch
        {
            VariableType.String => typeof(StringVariable),
            VariableType.Text => typeof(TextVariable),
            VariableType.Boolean => typeof(BooleanVariable),
            VariableType.Number => typeof(NumericVariable),
            VariableType.Date => typeof(DateVariable),
            _ => throw new NotImplementedException()
        };
    }

    private Dictionary<string, object> GetVariableComponentParameters(StepVariable customStepVariable)
    {
        return new Dictionary<string, object>() { { "Variable", customStepVariable } };
    }
}
