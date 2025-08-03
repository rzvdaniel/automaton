using AntDesign;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Automaton.Runner.Pages.Settings;

public partial class Settings : ComponentBase
{
    private bool loading = false;

    private readonly ILogger logger = Log.ForContext<Settings>();

    [Parameter] public SettingsViewModel SettingsViewModel { get; set; }

    [Inject] private MessageService MessageService { get; set; }
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
    }

    private async Task OnFinish(EditContext editContext)
    {
        try
        {
            loading = true;

            await SettingsViewModel.SaveSettings();

            await MessageService.InfoAsync("Runner settings updated");
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync("Runner settings update failed");
            logger.Error(ex, "Runner settings update failed");
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
