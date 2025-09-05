using Automaton.Client.Auth.Services;
using Automaton.Studio.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace Automaton.Studio.Services;

public class ApiKeysService : IApiKeysService
{
    private readonly HttpClient httpClient;
    private readonly ClientAuthConfigurationService configService;

    public ApiKeysService(HttpClient httpClient, ClientAuthConfigurationService configService)
    {
        this.httpClient = httpClient;
        this.configService = configService;
    }

    /// <summary>
    /// Get all API keys for the current user
    /// </summary>
    public async Task<List<ApiKeyResponse>> GetApiKeysAsync()
    {
        var response = await httpClient.GetAsync("/api/apikeys");
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        var apiKeys = JsonSerializer.Deserialize<List<ApiKeyResponse>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return apiKeys ?? new List<ApiKeyResponse>();
    }

    /// <summary>
    /// Create a new API key
    /// </summary>
    public async Task<CreateApiKeyResponse> CreateApiKeyAsync(CreateApiKeyRequest request)
    {
        var response = await httpClient.PostAsJsonAsync("/api/apikeys", request);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CreateApiKeyResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (result == null)
        {
            throw new InvalidOperationException("Failed to deserialize API key creation response");
        }

        return result;
    }

    /// <summary>
    /// Revoke an API key
    /// </summary>
    public async Task<bool> RevokeApiKeyAsync(Guid apiKeyId)
    {
        var response = await httpClient.DeleteAsync($"/api/apikeys/{apiKeyId}");
        return response.IsSuccessStatusCode;
    }
}