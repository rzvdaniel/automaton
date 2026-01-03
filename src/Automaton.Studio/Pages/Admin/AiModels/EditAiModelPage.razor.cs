using AntDesign;
using Automaton.Studio.Models.Admin;
using Automaton.Studio.Services.Admin;
using Microsoft.AspNetCore.Components;

namespace Automaton.Studio.Pages.Admin.AiModels
{
    partial class EditAiModelPage : ComponentBase
    {
        [Inject] private AdminAiProviderService AdminAiProviderService { get; set; } = default!;
        [Inject] private MessageService MessageService { get; set; } = default!;
        [Inject] private NavigationManager NavigationManager { get; set; } = default!;

        [Parameter] public Guid Id { get; set; }

        private AiModelFormModel? formModel;
        private bool loading = true;
        private bool isSaving = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadModel();
        }

        private async Task LoadModel()
        {
            loading = true;
            try
            {
                var models = await AdminAiProviderService.GetAllModelsAsync();
                var model = models.FirstOrDefault(m => m.Id == Id);

                if (model != null)
                {
                    formModel = AiModelFormModel.FromModel(model);
                }
            }
            catch (Exception ex)
            {
                _ = MessageService.ErrorAsync($"Failed to load AI model: {ex.Message}");
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
                if (string.IsNullOrWhiteSpace(formModel.ModelId))
                {
                    _ = MessageService.ErrorAsync("Model ID is required");
                    return;
                }

                if (string.IsNullOrWhiteSpace(formModel.DisplayName))
                {
                    _ = MessageService.ErrorAsync("Display Name is required");
                    return;
                }

                var request = formModel.ToUpdateRequest();
                var success = await AdminAiProviderService.UpdateModelAsync(Id, request);

                if (success)
                {
                    _ = MessageService.SuccessAsync("AI model updated successfully!");
                    NavigationManager.NavigateTo("/admin/ai-models");
                }
                else
                {
                    _ = MessageService.ErrorAsync("Failed to update AI model");
                }
            }
            catch (Exception ex)
            {
                _ = MessageService.ErrorAsync($"Failed to update AI model: {ex.Message}");
            }
            finally
            {
                isSaving = false;
            }
        }

        private void HandleCancel()
        {
            NavigationManager.NavigateTo("/admin/ai-models");
        }
    }
}
