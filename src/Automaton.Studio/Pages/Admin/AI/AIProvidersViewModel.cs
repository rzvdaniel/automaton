using AntDesign;
using Automaton.Studio.Models;
using Automaton.Mcp.Enums;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace Automaton.Studio.Pages.Admin.AI;

public class AIProvidersViewModel
{
    private readonly HttpClient httpClient;
    private readonly MessageService messageService;
    private readonly ILogger<AIProvidersViewModel> logger;

    public bool Loading { get; set; } = false;
    public bool ProviderModalVisible { get; set; } = false;
    public bool ModelModalVisible { get; set; } = false;
    public bool ProviderSaving { get; set; } = false;
    public bool ModelSaving { get; set; } = false;

    public List<AiProviderConfigModel> Providers { get; set; } = new();
    public AiProviderConfigModel? EditingProvider { get; set; }
    public AiModelConfigModel? EditingModel { get; set; }
    public Guid? CurrentProviderId { get; set; }

    public ProviderRequest ProviderRequest { get; set; } = new();
    public ModelRequest ModelRequest { get; set; } = new();

    public AIProvidersViewModel(
        HttpClient httpClient,
        MessageService messageService,
        ILogger<AIProvidersViewModel> logger)
    {
        this.httpClient = httpClient;
        this.messageService = messageService;
        this.logger = logger;
    }

    public async Task LoadProviders()
    {
        Loading = true;
        try
        {
            var response = await httpClient.GetAsync("/api/admin/ai-providers");
            response.EnsureSuccessStatusCode();

            Providers = await response.Content.ReadFromJsonAsync<List<AiProviderConfigModel>>() ?? new();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading AI providers");
            await messageService.ErrorAsync($"Failed to load AI providers: {ex.Message}");
        }
        finally
        {
            Loading = false;
        }
    }

    public void ShowCreateProviderModal()
    {
        EditingProvider = null;
        ProviderRequest = new ProviderRequest
        {
            Enabled = true,
            DisplayOrder = 0
        };
        ProviderModalVisible = true;
    }

    public void ShowEditProviderModal(AiProviderConfigModel provider)
    {
        EditingProvider = provider;
        ProviderRequest = new ProviderRequest
        {
            ProviderName = provider.ProviderName,
            Provider = provider.Provider,
            Description = provider.Description,
            DefaultModel = provider.DefaultModel,
            Enabled = provider.Enabled,
            DisplayOrder = provider.DisplayOrder
        };
        ProviderModalVisible = true;
    }

    public async Task SaveProvider()
    {
        ProviderSaving = true;
        try
        {
            if (EditingProvider == null)
            {
                // Create new provider
                var response = await httpClient.PostAsJsonAsync("/api/admin/ai-providers", ProviderRequest);
                response.EnsureSuccessStatusCode();
                await messageService.SuccessAsync("AI provider created successfully");
            }
            else
            {
                // Update existing provider
                var response = await httpClient.PutAsJsonAsync($"/api/admin/ai-providers/{EditingProvider.Id}", 
                    new UpdateProviderRequest
                    {
                        ProviderName = ProviderRequest.ProviderName,
                        Description = ProviderRequest.Description,
                        DefaultModel = ProviderRequest.DefaultModel,
                        Enabled = ProviderRequest.Enabled,
                        DisplayOrder = ProviderRequest.DisplayOrder
                    });
                response.EnsureSuccessStatusCode();
                await messageService.SuccessAsync("AI provider updated successfully");
            }

            ProviderModalVisible = false;
            await LoadProviders();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving AI provider");
            await messageService.ErrorAsync($"Failed to save AI provider: {ex.Message}");
        }
        finally
        {
            ProviderSaving = false;
        }
    }

    public async Task DeleteProvider(Guid providerId)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"/api/admin/ai-providers/{providerId}");
            response.EnsureSuccessStatusCode();
            await messageService.SuccessAsync("AI provider deleted successfully");
            await LoadProviders();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting AI provider");
            await messageService.ErrorAsync($"Failed to delete AI provider: {ex.Message}");
        }
    }

    public void ShowCreateModelModal(Guid providerId)
    {
        CurrentProviderId = providerId;
        EditingModel = null;
        ModelRequest = new ModelRequest
        {
            Enabled = true,
            DisplayOrder = 0
        };
        ModelModalVisible = true;
    }

    public void ShowEditModelModal(Guid providerId, AiModelConfigModel model)
    {
        CurrentProviderId = providerId;
        EditingModel = model;
        ModelRequest = new ModelRequest
        {
            ModelName = model.ModelName,
            DisplayName = model.DisplayName,
            Description = model.Description,
            Enabled = model.Enabled,
            DisplayOrder = model.DisplayOrder
        };
        ModelModalVisible = true;
    }

    public async Task SaveModel()
    {
        if (!CurrentProviderId.HasValue)
        {
            await messageService.ErrorAsync("Invalid provider");
            return;
        }

        ModelSaving = true;
        try
        {
            if (EditingModel == null)
            {
                // Create new model
                var response = await httpClient.PostAsJsonAsync($"/api/admin/ai-providers/{CurrentProviderId}/models", ModelRequest);
                response.EnsureSuccessStatusCode();
                await messageService.SuccessAsync("AI model created successfully");
            }
            else
            {
                // Update existing model
                var response = await httpClient.PutAsJsonAsync(
                    $"/api/admin/ai-providers/{CurrentProviderId}/models/{EditingModel.Id}",
                    ModelRequest);
                response.EnsureSuccessStatusCode();
                await messageService.SuccessAsync("AI model updated successfully");
            }

            ModelModalVisible = false;
            await LoadProviders();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving AI model");
            await messageService.ErrorAsync($"Failed to save AI model: {ex.Message}");
        }
        finally
        {
            ModelSaving = false;
        }
    }

    public async Task DeleteModel(Guid providerId, Guid modelId)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"/api/admin/ai-providers/{providerId}/models/{modelId}");
            response.EnsureSuccessStatusCode();
            await messageService.SuccessAsync("AI model deleted successfully");
            await LoadProviders();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting AI model");
            await messageService.ErrorAsync($"Failed to delete AI model: {ex.Message}");
        }
    }
}

// Request models
public class ProviderRequest
{
    public string ProviderName { get; set; } = string.Empty;
    public AiProviderType Provider { get; set; }
    public string? Description { get; set; }
    public string DefaultModel { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
}

public class UpdateProviderRequest
{
    public string ProviderName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DefaultModel { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int DisplayOrder { get; set; }
}

public class ModelRequest
{
    public string ModelName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public int DisplayOrder { get; set; } = 0;
}
