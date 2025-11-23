using AntDesign;
using Automaton.Studio.Pages.Install.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Automaton.Studio.Pages.Install;

partial class InstallPage : ComponentBase
{
    private bool loading = false;
    private bool installationComplete;

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private InstallViewModel InstallViewModel { get; set; } = default!;
    [Inject] private MessageService MessageService { get; set; }
    [Inject] private InstallService InstallService { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            installationComplete = await InstallService.IsInstallationComplete();
        }
        catch
        {
            // If we can't check status, assume installation is not complete
            installationComplete = false;
        }
        
        await base.OnInitializedAsync();
    }

    private async Task OnFinish(EditContext editContext)
    {
        try
        {
            loading = true;

            await InstallViewModel.Install();

            NavigationManager.NavigateTo("/");
        }
        catch (Exception ex)
        {
            _ = MessageService.ErrorAsync(Resources.Errors.InstallationFailed);
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
