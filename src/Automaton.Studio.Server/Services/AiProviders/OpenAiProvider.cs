using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Entities.Enums;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Automaton.Studio.Server.Services.AiProviders;

public class OpenAiProvider : IAiProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<OpenAiProvider> logger;
    private readonly AiProvidersConfig aiProvidersConfig;

    public AiProviderType ProviderType => AiProviderType.OpenAI;

    public string DefaultModel => aiProvidersConfig.GetProviderConfig(ProviderType)?.DefaultModel;
    
    public IReadOnlyList<string> SupportedModels
    {
        get
        {
            var configModels = aiProvidersConfig.GetProviderConfig(ProviderType)?.SupportedModels;
            return configModels;
        }
    }

    public OpenAiProvider(HttpClient httpClient, ILogger<OpenAiProvider> logger, IOptions<AiProvidersConfig> aiProvidersConfig)
    {
        this.httpClient = httpClient;
        this.logger = logger;
        this.aiProvidersConfig = aiProvidersConfig.Value;
    }

    public async Task<string> GenerateCodeAsync(string apiKey, string prompt, string model, List<ConversationMessage>? conversationHistory = null)
    {
        try
        {
            var messages = new List<object>();
            
            // Add conversation history if provided
            if (conversationHistory?.Any() == true)
            {
                foreach (var historyMessage in conversationHistory)
                {
                    messages.Add(new { role = historyMessage.Role.ToLowerInvariant(), content = historyMessage.Content });
                }
            }
            
            // Add the current prompt
            messages.Add(new { role = "user", content = prompt });

            var request = new
            {
                model = model,
                messages = messages.ToArray(),
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

    // Legacy method for backward compatibility
    public async Task<string> GenerateCodeAsync(string apiKey, string prompt, string model)
    {
        return await GenerateCodeAsync(apiKey, prompt, model, null);
    }
}