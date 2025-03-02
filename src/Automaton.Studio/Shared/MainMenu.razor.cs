using Automaton.App.Authentication.Services;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Shared;

public partial class MainMenu : ComponentBase
{
    [Parameter] public Guid Id { get; set; }
    [Parameter] public bool Collapsed { get; set; }

    [Inject] NavigationManager NavigationManager { get; set; } = default!;
    [Inject] AuthenticationService AuthenticationService { get; set; }

    public async Task Logout()
    {
        await AuthenticationService.Logout();

        NavigationManager.NavigateTo($"/");
    }
}

