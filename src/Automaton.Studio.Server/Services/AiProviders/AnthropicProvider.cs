using Automaton.Studio.Server.Entities;
using System.Text.Json;

namespace Automaton.Studio.Server.Services.AiProviders;

public class AnthropicProvider : IAiProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<AnthropicProvider> logger;

    public AiProvider ProviderType => AiProvider.Anthropic;
    public string DefaultModel => "claude-3-sonnet";

    public AnthropicProvider(HttpClient httpClient, ILogger<AnthropicProvider> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public async Task<string> GenerateCodeAsync(string apiKey, string prompt, string model)
    {
        try
        {
            var request = new
            {
                model = model,
                max_tokens = 2000,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
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
}