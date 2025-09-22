using Automaton.Studio.Models;
using Automaton.Studio.Services.AI;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Automaton.Studio.Services;

/// <summary>
/// Service for managing AI configuration
/// </summary>
public class AiConfigurationService : IAiConfigurationService
{
    private readonly HttpClient httpClient;
    private readonly ConfigurationService configService;
    private readonly IEnumerable<IAiStepGenerationProvider> aiProviders;
    private readonly IConfiguration configuration;

    public AiConfigurationService(
        HttpClient httpClient,
        ConfigurationService configService,
        IEnumerable<IAiStepGenerationProvider> aiProviders,
        IConfiguration configuration)
    {
        this.httpClient = httpClient;
        this.configService = configService;
        this.aiProviders = aiProviders;
        this.configuration = configuration;
    }

    public async Task<AiConfigurationResponse> GetConfigurationAsync()
    {
        try
        {
            var response = await httpClient.GetAsync($"{configService.BaseUrl}/api/ai/configuration");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var config = JsonSerializer.Deserialize<AiConfigurationResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return config ?? new AiConfigurationResponse();
            }
            
            // If API call fails, return local configuration
            return GetLocalConfiguration();
        }
        catch
        {
            // Fallback to local configuration if API is not available
            return GetLocalConfiguration();
        }
    }

    public async Task<bool> UpdateConfigurationAsync(UpdateAiConfigurationRequest request)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync($"{configService.BaseUrl}/api/ai/configuration", request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<Dictionary<string, bool>> TestProvidersAsync()
    {
        var results = new Dictionary<string, bool>();
        
        foreach (var provider in aiProviders)
        {
            try
            {
                results[provider.ProviderName] = await provider.IsAvailableAsync();
            }
            catch
            {
                results[provider.ProviderName] = false;
            }
        }
        
        return results;
    }

    private AiConfigurationResponse GetLocalConfiguration()
    {
        var openAiApiKey = configuration["OpenAI:ApiKey"] ?? 
                          configuration["OPENAI_API_KEY"] ?? 
                          Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        var openAiModel = configuration["OpenAI:Model"] ?? "gpt-4";

        return new AiConfigurationResponse
        {
            IsOpenAiConfigured = !string.IsNullOrWhiteSpace(openAiApiKey),
            OpenAiModel = openAiModel,
            OpenAiApiKeyMasked = MaskApiKey(openAiApiKey)
        };
    }

    private static string? MaskApiKey(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        if (apiKey.Length <= 8)
            return "sk-****";

        return $"{apiKey[..4]}****{apiKey[^4..]}";
    }
}