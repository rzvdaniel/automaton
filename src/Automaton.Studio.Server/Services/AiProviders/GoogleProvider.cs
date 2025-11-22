using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Entities.Enums;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Automaton.Studio.Server.Services.AiProviders;

public class GoogleProvider : IAiProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<GoogleProvider> logger;
    private readonly AiProvidersConfig aiProvidersConfig;

    public AiProviderType ProviderType => AiProviderType.Google;

    public string DefaultModel => aiProvidersConfig.GetProviderConfig(ProviderType)?.DefaultModel;
    
    public IReadOnlyList<string> SupportedModels
    {
        get
        {
            var configModels = aiProvidersConfig.GetProviderConfig(ProviderType)?.SupportedModels;
            return configModels;
        }
    }

    public GoogleProvider(HttpClient httpClient, ILogger<GoogleProvider> logger, IOptions<AiProvidersConfig> aiProvidersConfig)
    {
        this.httpClient = httpClient;
        this.logger = logger;
        this.aiProvidersConfig = aiProvidersConfig.Value;
    }

    public async Task<string> GenerateCodeAsync(string apiKey, string prompt, string model, List<ConversationMessage>? conversationHistory = null)
    {
        try
        {
            var contents = new List<object>();
            
            // Add conversation history if provided
            if (conversationHistory?.Any() == true)
            {
                foreach (var historyMessage in conversationHistory)
                {
                    var role = historyMessage.Role.ToLowerInvariant();
                    // Google uses "model" instead of "assistant"
                    if (role == "assistant" || role == "ai") role = "model";
                    
                    contents.Add(new
                    {
                        role = role,
                        parts = new[]
                        {
                            new { text = historyMessage.Content }
                        }
                    });
                }
            }
            
            // Add the current prompt
            contents.Add(new
            {
                role = "user",
                parts = new[]
                {
                    new { text = prompt }
                }
            });

            var request = new
            {
                contents = contents.ToArray(),
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

    // Legacy method for backward compatibility
    public async Task<string> GenerateCodeAsync(string apiKey, string prompt, string model)
    {
        return await GenerateCodeAsync(apiKey, prompt, model, null);
    }
}