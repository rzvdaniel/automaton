using AntDesign;
using Automaton.Studio.Pages.Runners;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.FlowDesigner
{
    public partial class FlowSettings : ComponentBase
    {
        [Parameter] public string FlowId { get; set; }
        [Parameter] public string FlowName { get; set; }

        public bool loading;

        [Inject] private NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private FlowSettingsViewModel ViewModel { get; set; } = default!;
        [Inject] private ModalService ModalService { get; set; }
        [Inject] private MessageService MessageService { get; set; }

        protected override async Task OnInitializedAsync()
        {
            loading = true;

            try
            {
                await ViewModel.GetFlowDashboard(FlowId);
            }
            catch
            {
                await MessageService.ErrorAsync(Resources.Errors.FlowsActivityNotLoaded);
            }
            finally
            {
                loading = false;
            }

            await base.OnInitializedAsync();
        }
    }
}
