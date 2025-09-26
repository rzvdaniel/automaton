using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Entities;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Automaton.Studio.Server.Services.AiProviders;

public class GoogleProvider : IAiProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<GoogleProvider> logger;
    private readonly AiProvidersConfig aiProvidersConfig;

    public AiProvider ProviderType => AiProvider.Google;

    private static readonly List<string> DefaultSupportedModels = new()
    {
        "gemini-1.5-pro",                // Latest pro model - most capable
        "gemini-1.5-flash",              // Fast and efficient
        "gemini-1.5-pro-exp-0827",      // Experimental pro version
        "gemini-pro",                    // Legacy model
        "gemini-pro-vision"              // Vision-capable legacy model
    };

    private const string DefaultModelName = "gemini-1.5-pro";

    public string DefaultModel => aiProvidersConfig.GetProviderConfig(ProviderType)?.DefaultModel ?? DefaultModelName;
    
    public IReadOnlyList<string> SupportedModels
    {
        get
        {
            var configModels = aiProvidersConfig.GetProviderConfig(ProviderType)?.SupportedModels;
            return configModels?.Count > 0 ? configModels : DefaultSupportedModels;
        }
    }

    public GoogleProvider(HttpClient httpClient, ILogger<GoogleProvider> logger, IOptions<AiProvidersConfig> aiProvidersConfig)
    {
        this.httpClient = httpClient;
        this.logger = logger;
        this.aiProvidersConfig = aiProvidersConfig.Value;
    }

    public async Task<string> GenerateCodeAsync(string apiKey, string prompt, string model)
    {
        try
        {
            var request = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    maxOutputTokens = 2000,
                    temperature = 0.7
                }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}");
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseJson = JsonDocument.Parse(responseContent);
            
            return responseJson.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error calling Google API with model {Model}", model);
            throw;
        }
    }
}