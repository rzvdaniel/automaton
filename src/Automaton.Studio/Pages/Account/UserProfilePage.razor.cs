using AntDesign;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Automaton.Studio.Pages.Account;

public partial class UserProfilePage : ComponentBase
{
    private bool loading = false;

    [Inject] private UserProfileViewModel UserProfileViewModel { get; set; } = default!;
    [Inject] private MessageService MessageService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await UserProfileViewModel.LoadUserProfile();

        await base.OnInitializedAsync();
    }

    private async Task OnFinish(EditContext editContext)
    {
        try
        {
            loading = true;

            await UserProfileViewModel.UpdateUserProfile();

            await MessageService.InfoAsync("User profile updated");
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync("User profile update failed");
        }
        finally
        {
            loading = false;
        }
    }

    private void OnFinishFailed(EditContext editContext)
    {
        // Do nothing if form validation fails
    }
}
