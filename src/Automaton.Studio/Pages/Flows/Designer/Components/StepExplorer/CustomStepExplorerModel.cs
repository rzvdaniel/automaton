using Automaton.Studio.Domain;

namespace Automaton.Studio.Pages.Flows.Designer.Components.StepExplorer;

public class CustomStepExplorerModel : StepExplorerModel
{
    public CustomStepDefinition Definition { get; set; } = new CustomStepDefinition();
}
