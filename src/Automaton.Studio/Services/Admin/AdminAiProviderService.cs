using Automaton.Studio.Models.Admin;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Automaton.Studio.Services.Admin;

public class AdminAiProviderService
{
    private readonly HttpClient httpClient;
    private readonly ILogger<AdminAiProviderService> logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AdminAiProviderService(HttpClient httpClient, ILogger<AdminAiProviderService> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    #region Providers

    public async Task<IEnumerable<AiProviderAdminModel>> GetProvidersAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("api/admin/ai-providers");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<IEnumerable<AiProviderAdminModel>>(json, JsonOptions)
                ?? new List<AiProviderAdminModel>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching AI providers");
            throw;
        }
    }

    public async Task<AiProviderAdminModel?> GetProviderAsync(Guid id)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/admin/ai-providers/{id}");
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<AiProviderAdminModel>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching AI provider {ProviderId}", id);
            throw;
        }
    }

    public async Task<AiProviderAdminModel?> CreateProviderAsync(AdminCreateAiProviderRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("api/admin/ai-providers", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Failed to create AI provider: {Error}", error);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<AiProviderAdminModel>(responseJson, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating AI provider");
            throw;
        }
    }

    public async Task<bool> UpdateProviderAsync(Guid id, AdminUpdateAiProviderRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PutAsync($"api/admin/ai-providers/{id}", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Failed to update AI provider {ProviderId}: {Error}", id, error);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating AI provider {ProviderId}", id);
            throw;
        }
    }

    public async Task<bool> DeleteProviderAsync(Guid id)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/admin/ai-providers/{id}");

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Failed to delete AI provider {ProviderId}: {Error}", id, error);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting AI provider {ProviderId}", id);
            return false;
        }
    }

    #endregion

    #region Models

    public async Task<IEnumerable<AiModelAdminModel>> GetModelsAsync(Guid providerId)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/admin/ai-providers/{providerId}/models");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<IEnumerable<AiModelAdminModel>>(json, JsonOptions)
                ?? new List<AiModelAdminModel>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching AI models for provider {ProviderId}", providerId);
            throw;
        }
    }

    public async Task<IEnumerable<AiModelAdminModel>> GetAllModelsAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("api/admin/ai-models");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<IEnumerable<AiModelAdminModel>>(json, JsonOptions)
                ?? new List<AiModelAdminModel>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching all AI models");
            throw;
        }
    }

    public async Task<AiModelAdminModel?> CreateModelAsync(Guid providerId, AdminCreateAiModelRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync($"api/admin/ai-providers/{providerId}/models", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Failed to create AI model: {Error}", error);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<AiModelAdminModel>(responseJson, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating AI model for provider {ProviderId}", providerId);
            throw;
        }
    }

    public async Task<bool> UpdateModelAsync(Guid id, AdminUpdateAiModelRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, JsonOptions);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PutAsync($"api/admin/ai-models/{id}", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Failed to update AI model {ModelId}: {Error}", id, error);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating AI model {ModelId}", id);
            throw;
        }
    }

    public async Task<bool> DeleteModelAsync(Guid id)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/admin/ai-models/{id}");

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Failed to delete AI model {ModelId}: {Error}", id, error);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting AI model {ModelId}", id);
            return false;
        }
    }

    #endregion
}
