using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Projects;

public partial class ProjectPage : ComponentBase
{
    [Parameter]
    public string ProjectId { get; set; } = string.Empty;

    [Inject]
    public ProjectSettingsViewModel ViewModel { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await ViewModel.InitializeAsync(Guid.Parse(ProjectId));
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Guid.TryParse(ProjectId, out var projectId))
        {
            await ViewModel.InitializeAsync(projectId);
        }
    }
}
