using AntDesign;
using Automaton.Studio.Models.Admin;
using Automaton.Studio.Services.Admin;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Admin.AiProviders
{
    partial class EditAiProviderPage : ComponentBase
    {
        [Inject] private AdminAiProviderService AdminAiProviderService { get; set; } = default!;
        [Inject] private MessageService MessageService { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        [Parameter] public Guid Id { get; set; }

        private AiProviderFormModel? formModel;
        private bool loading = true;
        private bool isSaving = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadProvider();
        }

        private async Task LoadProvider()
        {
            loading = true;
            try
            {
                var providers = await AdminAiProviderService.GetProvidersAsync();
                var provider = providers.FirstOrDefault(p => p.Id == Id);

                if (provider != null)
                {
                    formModel = AiProviderFormModel.FromProvider(provider);
                }
            }
            catch (Exception ex)
            {
                _ = MessageService.ErrorAsync($"Failed to load AI provider: {ex.Message}");
            }
            finally
            {
                loading = false;
            }
        }

        private async Task HandleUpdate()
        {
            if (formModel == null) return;

            isSaving = true;
            try
            {
                if (string.IsNullOrWhiteSpace(formModel.Name))
                {
                    _ = MessageService.ErrorAsync("Name is required");
                    return;
                }

                if (string.IsNullOrWhiteSpace(formModel.DisplayName))
                {
                    _ = MessageService.ErrorAsync("Display Name is required");
                    return;
                }

                var request = formModel.ToUpdateRequest();
                var success = await AdminAiProviderService.UpdateProviderAsync(Id, request);

                if (success)
                {
                    _ = MessageService.SuccessAsync("AI provider updated successfully!");
                    NavigationManager.NavigateTo("/admin/ai-providers");
                }
                else
                {
                    _ = MessageService.ErrorAsync("Failed to update AI provider");
                }
            }
            catch (Exception ex)
            {
                _ = MessageService.ErrorAsync($"Failed to update AI provider: {ex.Message}");
            }
            finally
            {
                isSaving = false;
            }
        }

        private void HandleCancel()
        {
            NavigationManager.NavigateTo("/admin/ai-providers");
        }
    }
}
