using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.FlowDesigner.Components.StepExplorer;

partial class CustomStepsExplorer : ComponentBase
{
    [Inject] 
    private CustomStepsViewModel CustomStepsViewModel { get; set; } = default!;

    private string searchText;

    protected override async Task OnInitializedAsync()
    {
        CustomStepsViewModel.Initialize();

        await base.OnInitializedAsync();
    }
}