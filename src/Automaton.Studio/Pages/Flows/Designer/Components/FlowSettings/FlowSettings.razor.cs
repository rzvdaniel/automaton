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
            await LoadFlowSettings();
            await base.OnInitializedAsync();
        }

        protected override async Task OnParametersSetAsync()
        {
            await LoadFlowSettings();
            await base.OnParametersSetAsync();
        }

        private async Task LoadFlowSettings()
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
                _ = MessageService.ErrorAsync(Resources.Errors.FlowsActivityNotLoaded);
            }
            finally
            {
                loading = false;
            }
        }

        private async Task HandleUpdateSettings()
        {
            isSaving = true;

            try
            {
                await ViewModel.UpdateFlowSettings(settingsModel.Name);
                _ = MessageService.SuccessAsync("Flow settings updated successfully");
            }
            catch
            {
                _ = MessageService.ErrorAsync("Failed to update flow settings");
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
