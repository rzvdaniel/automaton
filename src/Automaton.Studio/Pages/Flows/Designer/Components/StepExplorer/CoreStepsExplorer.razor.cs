using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Flows.Designer.Components.StepExplorer;

partial class CoreStepsExplorer : ComponentBase
{
    [Inject] 
    private CoreStepsViewModel CoreStepsViewModel { get; set; } = default!;

    private string searchText;

    protected override async Task OnInitializedAsync()
    {
        CoreStepsViewModel.Initialize();

        await base.OnInitializedAsync();
    }
}