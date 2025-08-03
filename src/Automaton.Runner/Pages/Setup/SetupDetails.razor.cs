using AntDesign;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Automaton.Runner.Pages.Setup;

public partial class SetupDetails : ComponentBase
{
    private bool loading = false;
    private readonly ILogger logger = Log.ForContext<SetupDetails>();

    [Parameter] public SetupViewModel SetupViewModel { get; set; }

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

            await SetupViewModel.SetupRunner();

            await MessageService.InfoAsync("Runner Registered");

            NavigationManager.NavigateTo($"/");
        }
        catch (Exception ex)
        {
            await MessageService.ErrorAsync("Runner registration failed");
            logger.Error(ex, "Runner registration failed");
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
