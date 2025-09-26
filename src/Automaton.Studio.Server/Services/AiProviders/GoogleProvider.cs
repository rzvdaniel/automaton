using Automaton.Studio.Server.Entities;
using System.Text.Json;

namespace Automaton.Studio.Server.Services.AiProviders;

public class GoogleProvider : IAiProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<GoogleProvider> logger;

    public AiProvider ProviderType => AiProvider.Google;
    public string DefaultModel => "gemini-pro";

    public GoogleProvider(HttpClient httpClient, ILogger<GoogleProvider> logger)
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