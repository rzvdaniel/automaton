using AntDesign;
using Automaton.Studio.Models.Admin;
using Automaton.Studio.Services.Admin;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Admin.AiProviders
{
    partial class CreateAiProviderPage : ComponentBase
    {
        [Inject] private AdminAiProviderService AdminAiProviderService { get; set; } = default!;
        [Inject] private MessageService MessageService { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        private AiProviderFormModel formModel = new();
        private bool isSaving = false;

        protected override void OnInitialized()
        {
            // Initialize with defaults
            formModel = new AiProviderFormModel
            {
                IsActive = true,
                RequiresApiKey = true,
                SortOrder = 0
            };
        }

        private async Task HandleCreate()
        {
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

                var request = formModel.ToCreateRequest();
                var newProvider = await AdminAiProviderService.CreateProviderAsync(request);

                if (newProvider != null)
                {
                    _ = MessageService.SuccessAsync("AI provider created successfully!");
                    NavigationManager.NavigateTo("/admin/ai-providers");
                }
                else
                {
                    _ = MessageService.ErrorAsync("Failed to create AI provider");
                }
            }
            catch (Exception ex)
            {
                _ = MessageService.ErrorAsync($"Failed to create AI provider: {ex.Message}");
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
