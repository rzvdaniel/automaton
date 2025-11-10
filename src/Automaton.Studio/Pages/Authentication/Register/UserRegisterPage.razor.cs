using AntDesign;
using Automaton.App.Authentication.Config;
using Automaton.Studio.Pages.Authentication.Config;
using Automaton.Studio.Pages.Authentication.Register;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Automaton.Studio.Pages.Authentication.Register;

partial class UserRegisterPage : ComponentBase
{
    private bool loading = false;

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private UserRegisterViewModel UserRegisterViewModel { get; set; } = default!;
    [Inject] private MessageService MessageService { get; set; }
    [Inject] private ConfigurationService ConfigurationService { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (ConfigurationService.NoUserSignUp)
        {
            NavigationManager.NavigateTo("/registernotavailable");
        }

        await base.OnInitializedAsync();
    }

    private async Task OnFinish(EditContext editContext)
    {
        try
        {
            loading = true;

            await UserRegisterViewModel.Register();

            NavigationManager.NavigateTo("/");
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync(Resources.Errors.UserRegistrationFailed);
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
