using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Account;

partial class AccountPage : ComponentBase
{
    private Type accountSection = typeof(UserProfilePage);

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
    }

    private void NavigateToUserProfile()
    {
        accountSection = typeof(UserProfilePage);
    }

    private void NavigateToUserSecurity()
    {
        accountSection = typeof(UserSecurityPage);
    }
}
