using Automaton.Studio.Pages.Authentication.Services;
using Automaton.Studio.Services;
using Automaton.Studio.Enums;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Shared;

public partial class MainMenu : ComponentBase
{
    [Parameter] public Guid Id { get; set; }
    [Parameter] public bool Collapsed { get; set; }

    [Inject] NavigationManager NavigationManager { get; set; } = default!;
    [Inject] AuthenticationService AuthenticationService { get; set; }
    [Inject] ProjectContextService ProjectContextService { get; set; }

    private bool showDevTools = true;
    private ProjectRole? currentUserRole;

    protected override void OnInitialized()
    {
        // Subscribe to project context changes
        ProjectContextService.OnCurrentProjectChanged += HandleProjectChanged;
        UpdateNavigationVisibility();
    }

    private void HandleProjectChanged(object? project)
    {
        UpdateNavigationVisibility();
        StateHasChanged();
    }

    private void UpdateNavigationVisibility()
    {
        if (ProjectContextService.IsInProjectContext())
        {
            currentUserRole = ProjectContextService.CurrentProject?.CurrentUserRole;

            // Show dev tools only for ProjectAdmin and Developer roles
            showDevTools = currentUserRole == ProjectRole.ProjectAdmin ||
                          currentUserRole == ProjectRole.Developer;
        }
        else
        {
            // Personal workspace - show all dev tools
            showDevTools = true;
            currentUserRole = null;
        }
    }

    public async Task Logout()
    {
        await AuthenticationService.Logout();

        NavigationManager.NavigateTo($"/");
    }

    public void Dispose()
    {
        ProjectContextService.OnCurrentProjectChanged -= HandleProjectChanged;
    }
}

