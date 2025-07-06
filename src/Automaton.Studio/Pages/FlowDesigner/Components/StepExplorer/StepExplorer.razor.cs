using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.FlowDesigner.Components.StepExplorer;

partial class StepExplorer : ComponentBase
{
    [Inject] 
    private StepsViewModel StepsViewModel { get; set; } = default!;

    private string searchText;

    protected override async Task OnInitializedAsync()
    {
        StepsViewModel.Initialize();

        await base.OnInitializedAsync();
    }
}
