using AntDesign;
using Automaton.App.Install.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Automaton.App.Install.Pages.Register;

partial class InstallPage : ComponentBase
{
    private bool loading = false;

    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private InstallViewModel InstallViewModel { get; set; } = default!;
    [Inject] private MessageService MessageService { get; set; }
    [Inject] private InstallService InstallService { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
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
            await MessageService.ErrorAsync(Resources.Errors.InstallationFailed);
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
