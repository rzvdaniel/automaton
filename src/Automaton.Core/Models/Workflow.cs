using Automaton.Core.Events;
using System.Linq.Dynamic.Core;

namespace Automaton.Core.Models;

public class Workflow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string StartupDefinitionId { get; set; } = string.Empty;

    public IDictionary<string, StepVariable> Variables { get; set; }

    public IDictionary<string, StepVariable> OutputVariables { get; set; }

    public IDictionary<string, StepVariable> InputVariables { get; set; }

    public List<WorkflowDefinition> Definitions { get; set; }

    public event EventHandler<SetVariableNotification>? SetWorkflowVariable;

    public Workflow()
    {
        Variables = new Dictionary<string, StepVariable>();
        OutputVariables = new Dictionary<string, StepVariable>();
        InputVariables = new Dictionary<string, StepVariable>();
        Definitions = new List<WorkflowDefinition>();
    }

    public WorkflowDefinition? GetStartupDefinition()
    {
        return Definitions.SingleOrDefault(x => x.Id == StartupDefinitionId);
    }

    public bool VariableWithNameExists(string name)
    {
        return Variables.Any(x => x.Value.Name == name);
    }

    public StepVariable? GetVariableByName(string name)
    {
        return Variables.Values.SingleOrDefault(variable => variable.Name == name);
    }

    public IEnumerable<StepVariable> GetVariables(IEnumerable<string> names)
    {
        var variables = Variables.Where(x => names.Contains(x.Key)).Select(x => x.Value);

        return variables;
    }

    public void SetVariable(StepVariable variable)
    {
        if (string.IsNullOrEmpty(variable.Id))
            throw new ArgumentException("A workflow variable must have an id before it can be added.", nameof(variable));

        if (Variables.ContainsKey(variable.Id))
        {
            Variables[variable.Id] = variable;
        }
        else
        {
            Variables.Add(variable.Id, variable);
        }

        SetWorkflowVariable?.Invoke(this, new SetVariableNotification(variable));
    }
}
