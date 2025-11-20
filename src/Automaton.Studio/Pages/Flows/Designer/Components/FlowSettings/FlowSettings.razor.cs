using AntDesign;
using Automaton.Studio.Models;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Flows.Designer.Components.FlowSettings
{
    public partial class FlowSettings : ComponentBase
    {
        [Parameter] public string FlowId { get; set; }
        [Parameter] public string FlowName { get; set; }

        public bool loading;
        public bool isSaving;

        [Inject] private NavigationManager NavigationManager { get; set; } = default!;
        [Inject] private FlowSettingsViewModel ViewModel { get; set; } = default!;
        [Inject] private ModalService ModalService { get; set; }
        [Inject] private MessageService MessageService { get; set; }

        private FlowSettingsModel settingsModel = new();

        protected override async Task OnInitializedAsync()
        {
            loading = true;

            try
            {
                await ViewModel.GetFlowDashboard(FlowId);
                
                // Initialize form model
                settingsModel = new FlowSettingsModel
                {
                    Name = ViewModel.Name
                };
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

        private async Task HandleUpdateSettings()
        {
            isSaving = true;

            try
            {
                await ViewModel.UpdateFlowSettings(settingsModel.Name);
                await MessageService.SuccessAsync("Flow settings updated successfully");
            }
            catch
            {
                await MessageService.ErrorAsync("Failed to update flow settings");
            }
            finally
            {
                isSaving = false;
            }
        }

        private class FlowSettingsModel
        {
            public string Name { get; set; }
        }
    }
}
