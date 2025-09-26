using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Entities;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Automaton.Studio.Server.Services.AiProviders;

public class OpenAiProvider : IAiProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<OpenAiProvider> logger;
    private readonly AiProvidersConfig aiProvidersConfig;

    public AiProvider ProviderType => AiProvider.OpenAI;

    private static readonly List<string> DefaultSupportedModels = new()
    {
        "gpt-4o",                    // Latest, most capable model
        "gpt-4o-mini",               // Fast and cost-effective  
        "gpt-4-turbo",               // Previous generation turbo
        "gpt-4",                     // Standard GPT-4
        "gpt-3.5-turbo"             // Legacy but cost-effective
    };

    private const string DefaultModelName = "gpt-4o";

    public string DefaultModel => aiProvidersConfig.GetProviderConfig(ProviderType)?.DefaultModel ?? DefaultModelName;
    
    public IReadOnlyList<string> SupportedModels
    {
        get
        {
            var configModels = aiProvidersConfig.GetProviderConfig(ProviderType)?.SupportedModels;
            return configModels?.Count > 0 ? configModels : DefaultSupportedModels;
        }
    }

    public OpenAiProvider(HttpClient httpClient, ILogger<OpenAiProvider> logger, IOptions<AiProvidersConfig> aiProvidersConfig)
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
                model = model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                max_tokens = 2000,
                temperature = 0.7
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseJson = JsonDocument.Parse(responseContent);
            
            return responseJson.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error calling OpenAI API with model {Model}", model);
            throw;
        }
    }
}