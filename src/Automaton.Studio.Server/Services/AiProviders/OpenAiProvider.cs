using Automaton.Studio.Server.Entities;
using System.Text.Json;

namespace Automaton.Studio.Server.Services.AiProviders;

public class OpenAiProvider : IAiProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<OpenAiProvider> logger;

    public AiProvider ProviderType => AiProvider.OpenAI;
    public string DefaultModel => "gpt-4";

    public OpenAiProvider(HttpClient httpClient, ILogger<OpenAiProvider> logger)
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