using AntDesign;
using Automaton.Studio.Models.Admin;
using Automaton.Studio.Services.Admin;

namespace Automaton.Studio.Pages.Admin.AiModels;

public class AdminAiModelsViewModel
{
    private readonly AdminAiProviderService adminAiProviderService;
    private readonly MessageService messageService;

    public bool Loading { get; set; } = false;

    public IEnumerable<AiModelAdminModel> Models { get; set; } = new List<AiModelAdminModel>();
    public IEnumerable<AiProviderAdminModel> Providers { get; set; } = new List<AiProviderAdminModel>();
    public Guid? SelectedProviderId { get; set; }

    public AdminAiModelsViewModel(
        AdminAiProviderService adminAiProviderService,
        MessageService messageService)
    {
        this.adminAiProviderService = adminAiProviderService;
        this.messageService = messageService;
    }

    public async Task LoadModels(Guid? providerId = null)
    {
        Loading = true;
        try
        {
            SelectedProviderId = providerId;

            // Load providers for filter
            Providers = await adminAiProviderService.GetProvidersAsync();

            if (providerId.HasValue)
            {
                Models = await adminAiProviderService.GetModelsAsync(providerId.Value);
            }
            else
            {
                Models = await adminAiProviderService.GetAllModelsAsync();
            }
        }
        catch (Exception ex)
        {
            _ = messageService.ErrorAsync($"Failed to load AI models: {ex.Message}");
        }
        finally
        {
            Loading = false;
        }
    }

    public async Task DeleteModel(Guid id)
    {
        try
        {
            var success = await adminAiProviderService.DeleteModelAsync(id);
            if (success)
            {
                var list = Models.ToList();
                list.RemoveAll(m => m.Id == id);
                Models = list;

                _ = messageService.SuccessAsync("AI model deleted successfully!");
            }
            else
            {
                _ = messageService.ErrorAsync("Failed to delete AI model");
            }
        }
        catch (Exception ex)
        {
            _ = messageService.ErrorAsync($"Failed to delete AI model: {ex.Message}");
        }
    }

    public async Task ToggleModelStatus(AiModelAdminModel model)
    {
        try
        {
            var request = new AdminUpdateAiModelRequest
            {
                IsActive = !model.IsActive
            };

            var success = await adminAiProviderService.UpdateModelAsync(model.Id, request);

            if (success)
            {
                model.IsActive = !model.IsActive;
                var status = model.IsActive ? "activated" : "deactivated";
                _ = messageService.SuccessAsync($"AI model {status} successfully!");
            }
            else
            {
                _ = messageService.ErrorAsync("Failed to update model status");
            }
        }
        catch (Exception ex)
        {
            _ = messageService.ErrorAsync($"Failed to update model status: {ex.Message}");
        }
    }

    public async Task SetAsDefault(AiModelAdminModel model)
    {
        try
        {
            var request = new AdminUpdateAiModelRequest
            {
                IsDefault = true
            };

            var success = await adminAiProviderService.UpdateModelAsync(model.Id, request);

            if (success)
            {
                // Refresh the list to show updated default status
                await LoadModels(SelectedProviderId);
                _ = messageService.SuccessAsync($"'{model.DisplayName}' is now the default model for {model.ProviderName}");
            }
            else
            {
                _ = messageService.ErrorAsync("Failed to set default model");
            }
        }
        catch (Exception ex)
        {
            _ = messageService.ErrorAsync($"Failed to set default model: {ex.Message}");
        }
    }
}
