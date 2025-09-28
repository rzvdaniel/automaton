using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Entities;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Automaton.Studio.Server.Services.AiProviders;

public class AnthropicProvider : IAiProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<AnthropicProvider> logger;
    private readonly AiProvidersConfig aiProvidersConfig;

    public AiProvider ProviderType => AiProvider.Anthropic;

    private static readonly List<string> DefaultSupportedModels = new()
    {
        "claude-3-5-sonnet-20241022",    // Latest Sonnet - best balance
        "claude-3-5-haiku-20241022",     // Latest Haiku - fastest
        "claude-3-opus-20240229",        // Most capable but expensive
        "claude-3-sonnet-20240229",      // Previous Sonnet
        "claude-3-haiku-20240307"        // Previous Haiku
    };

    private const string DefaultModelName = "claude-3-5-sonnet-20241022";

    public string DefaultModel => aiProvidersConfig.GetProviderConfig(ProviderType)?.DefaultModel ?? DefaultModelName;
    
    public IReadOnlyList<string> SupportedModels
    {
        get
        {
            var configModels = aiProvidersConfig.GetProviderConfig(ProviderType)?.SupportedModels;
            return configModels?.Count > 0 ? configModels : DefaultSupportedModels;
        }
    }

    public AnthropicProvider(HttpClient httpClient, ILogger<AnthropicProvider> logger, IOptions<AiProvidersConfig> aiProvidersConfig)
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
                    var role = historyMessage.Role.ToLowerInvariant();
                    // Anthropic uses "assistant" instead of "ai"
                    if (role == "ai") role = "assistant";
                    messages.Add(new { role = role, content = historyMessage.Content });
                }
            }
            
            // Add the current prompt
            messages.Add(new { role = "user", content = prompt });

            var request = new
            {
                model = model,
                max_tokens = 2000,
                messages = messages.ToArray()
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            httpRequest.Headers.Add("x-api-key", apiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseJson = JsonDocument.Parse(responseContent);
            
            return responseJson.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error calling Anthropic API with model {Model}", model);
            throw;
        }
    }

    // Legacy method for backward compatibility
    public async Task<string> GenerateCodeAsync(string apiKey, string prompt, string model)
    {
        return await GenerateCodeAsync(apiKey, prompt, model, null);
    }
}