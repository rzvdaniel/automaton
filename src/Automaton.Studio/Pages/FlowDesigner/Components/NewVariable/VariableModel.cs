namespace Automaton.Studio.Pages.FlowDesigner.Components.NewVariable;

public class VariableModel
{
    public string Name { get; set; }
    public object Value { get; set; }
    public string Description { get; set; }
    public IEnumerable<string> ExistingNames { get; set; }
}
