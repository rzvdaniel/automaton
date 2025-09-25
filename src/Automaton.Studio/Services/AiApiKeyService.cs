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

    public async Task<CodeGenerationResponse> GenerateCodeAsync(CodeGenerationRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync("api/aiApiKeys/generate-code", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<CodeGenerationResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result ?? new CodeGenerationResponse { IsSuccess = false, ErrorMessage = "Unknown error occurred" };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating code with AI");
            return new CodeGenerationResponse 
            { 
                IsSuccess = false, 
                ErrorMessage = $"Code generation failed: {ex.Message}" 
            };
        }
    }
}