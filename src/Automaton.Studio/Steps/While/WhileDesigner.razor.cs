using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Steps.While;

public partial class WhileDesigner : ComponentBase
{
    [Parameter]
    public WhileStep Step { get; set; }

    protected override void OnInitialized()
    {
        base.OnInitialized();
    }
}
