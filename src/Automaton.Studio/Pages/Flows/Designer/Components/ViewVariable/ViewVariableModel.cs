using Automaton.Core.Models;
using Automaton.Studio.Domain;

namespace Automaton.Studio.Pages.Flows.Designer.Components.ViewVariable;

public class ViewVariableModel
{
    public StepVariable Variable { get; set; }
    public StudioFlow Flow { get; set; }
}
