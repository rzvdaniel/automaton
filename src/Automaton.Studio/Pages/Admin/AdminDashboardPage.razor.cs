using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Admin
{
    partial class AdminDashboardPage : ComponentBase
    {
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        private void NavigateTo(string url)
        {
            NavigationManager.NavigateTo(url);
        }
    }
}
