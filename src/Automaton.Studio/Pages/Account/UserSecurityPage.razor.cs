using AntDesign;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Automaton.Studio.Pages.Account;

public partial class UserSecurityPage : ComponentBase
{
    private bool loading = false;

    [Inject] private UserSecurityViewModel UserSecurityViewModel { get; set; } = default!;
    [Inject] private MessageService MessageService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
    }

    private async Task OnFinish(EditContext editContext)
    {
        try
        {
            loading = true;

            await UserSecurityViewModel.UpdateUserPassword();

            await MessageService.InfoAsync("User password updated");
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync("User password update failed");
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
