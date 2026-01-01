using AntDesign;
using Automaton.Studio.Models.Admin;
using Automaton.Studio.Services.Admin;

namespace Automaton.Studio.Pages.Admin.AiModels;

public class AdminAiModelsViewModel
{
    private readonly AdminAiProviderService adminAiProviderService;
    private readonly MessageService messageService;

    public bool Loading { get; set; } = false;
    public bool CreateModalVisible { get; set; } = false;
    public bool EditModalVisible { get; set; } = false;
    public bool CreateLoading { get; set; } = false;
    public bool EditLoading { get; set; } = false;

    public IEnumerable<AiModelAdminModel> Models { get; set; } = new List<AiModelAdminModel>();
    public IEnumerable<AiProviderAdminModel> Providers { get; set; } = new List<AiProviderAdminModel>();
    public AdminCreateAiModelRequest CreateRequest { get; set; } = new();
    public AdminUpdateAiModelRequest EditRequest { get; set; } = new();
    public AiModelAdminModel? SelectedModel { get; set; }
    public Guid? SelectedProviderId { get; set; }
    public Guid CreateProviderId { get; set; }

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

            // Load providers for filter and create modal
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

    public void ShowCreateModal()
    {
        CreateRequest = new AdminCreateAiModelRequest
        {
            IsActive = true,
            IsDefault = false,
            MaxTokens = 4096,
            SupportsStreaming = true,
            SupportsTools = false,
            SupportsVision = false,
            SortOrder = 0
        };
        CreateProviderId = SelectedProviderId ?? (Providers.FirstOrDefault()?.Id ?? Guid.Empty);
        CreateModalVisible = true;
    }

    public void ShowEditModal(AiModelAdminModel model)
    {
        SelectedModel = model;
        EditRequest = new AdminUpdateAiModelRequest
        {
            ModelId = model.ModelId,
            DisplayName = model.DisplayName,
            Description = model.Description,
            IsActive = model.IsActive,
            IsDefault = model.IsDefault,
            MaxTokens = model.MaxTokens,
            SupportsStreaming = model.SupportsStreaming,
            SupportsTools = model.SupportsTools,
            SupportsVision = model.SupportsVision,
            CostPer1kTokensInput = model.CostPer1kTokensInput.Value,
            CostPer1kTokensOutput = model.CostPer1kTokensOutput.Value,
            SortOrder = model.SortOrder
        };
        EditModalVisible = true;
    }

    public async Task CreateModel()
    {
        CreateLoading = true;
        try
        {
            if (CreateProviderId == Guid.Empty)
            {
                _ = messageService.ErrorAsync("Please select a provider");
                return;
            }

            if (string.IsNullOrWhiteSpace(CreateRequest?.ModelId))
            {
                _ = messageService.ErrorAsync("Model ID is required");
                return;
            }

            if (string.IsNullOrWhiteSpace(CreateRequest?.DisplayName))
            {
                _ = messageService.ErrorAsync("Display Name is required");
                return;
            }

            var newModel = await adminAiProviderService.CreateModelAsync(CreateProviderId, CreateRequest);

            if (newModel != null)
            {
                var list = Models.ToList();
                list.Add(newModel);
                Models = list.OrderBy(m => m.ProviderName).ThenBy(m => m.SortOrder).ThenBy(m => m.ModelId);

                CreateModalVisible = false;
                _ = messageService.SuccessAsync("AI model created successfully!");
            }
            else
            {
                _ = messageService.ErrorAsync("Failed to create AI model");
            }
        }
        catch (Exception ex)
        {
            _ = messageService.ErrorAsync($"Failed to create AI model: {ex.Message}");
        }
        finally
        {
            CreateLoading = false;
        }
    }

    public async Task UpdateModel()
    {
        if (SelectedModel == null) return;

        EditLoading = true;
        try
        {
            var success = await adminAiProviderService.UpdateModelAsync(SelectedModel.Id, EditRequest);

            if (success)
            {
                // Reload to get updated data
                await LoadModels(SelectedProviderId);
                EditModalVisible = false;
                _ = messageService.SuccessAsync("AI model updated successfully!");
            }
            else
            {
                _ = messageService.ErrorAsync("Failed to update AI model");
            }
        }
        catch (Exception ex)
        {
            _ = messageService.ErrorAsync($"Failed to update AI model: {ex.Message}");
        }
        finally
        {
            EditLoading = false;
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
