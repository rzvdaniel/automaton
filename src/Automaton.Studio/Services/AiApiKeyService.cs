using Automaton.Studio.Models;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Automaton.Studio.Services;

public class AiApiKeyService
{
    private readonly HttpClient httpClient;
    private readonly ILogger<AiApiKeyService> logger;

    public AiApiKeyService(HttpClient httpClient, ILogger<AiApiKeyService> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public async Task<IEnumerable<AiApiKeyModel>> GetAiApiKeysAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("api/aiApiKeys");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var apiKeys = JsonSerializer.Deserialize<IEnumerable<AiApiKeyModel>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return apiKeys ?? new List<AiApiKeyModel>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching AI API keys");
            throw;
        }
    }

    public async Task<IEnumerable<AiProviderInfo>> GetAvailableProvidersAsync(bool includeEnvironment = false)
    {
        try
        {
            var response = await httpClient.GetAsync($"api/aiApiKeys/providers?includeEnvironment={includeEnvironment}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var providers = JsonSerializer.Deserialize<IEnumerable<AiProviderInfo>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return providers ?? new List<AiProviderInfo>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching AI providers");
            throw;
        }
    }

    public async Task<AiApiKeyModel> CreateAiApiKeyAsync(CreateAiApiKeyRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("api/aiApiKeys", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var apiKey = JsonSerializer.Deserialize<AiApiKeyModel>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return apiKey!;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating AI API key");
            throw;
        }
    }

    public async Task<bool> DeleteAiApiKeyAsync(Guid id)
    {
        try
        {
            var response = await httpClient.DeleteAsync($"api/aiApiKeys/{id}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting AI API key {AiApiKeyId}", id);
            return false;
        }
    }

    /// <summary>
    /// Get decrypted API key for MCP integration
    /// </summary>
    public async Task<string?> GetDecryptedApiKeyAsync(Guid id)
    {
        try
        {
            var response = await httpClient.PostAsync($"api/aiApiKeys/{id}/decrypt", null);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to get decrypted API key {ApiKeyId}: {StatusCode}", id, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<DecryptedApiKeyResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result?.ApiKey;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting decrypted API key {ApiKeyId}", id);
            return null;
        }
    }

    /// <summary>
    /// Get environment API keys (for desktop scenarios)
    /// </summary>
    public async Task<Dictionary<AiProvider, string?>> GetEnvironmentApiKeysAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("api/aiApiKeys/environment");
            if (!response.IsSuccessStatusCode)
            {
                return new Dictionary<AiProvider, string?>();
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<Dictionary<AiProvider, string?>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result ?? new Dictionary<AiProvider, string?>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting environment API keys");
            return new Dictionary<AiProvider, string?>();
        }
    }

    /// <summary>
    /// Get API keys by provider for MCP integration
    /// </summary>
    public async Task<IEnumerable<AiApiKeyModel>> GetAiApiKeysByProviderAsync(AiProvider provider)
    {
        try
        {
            var allKeys = await GetAiApiKeysAsync();
            return allKeys.Where(k => k.Provider == provider && k.IsActive);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting API keys for provider {Provider}", provider);
            return new List<AiApiKeyModel>();
        }
    }
}

// Additional DTOs
public class DecryptedApiKeyResponse
{
    public string ApiKey { get; set; } = string.Empty;
}