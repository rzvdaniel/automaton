using Automaton.Studio.Server.Entities;

namespace Automaton.Studio.Server.Services.AiProviders;

public class AzureOpenAiProvider : IAiProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<AzureOpenAiProvider> logger;

    public AiProvider ProviderType => AiProvider.Azure;
    public string DefaultModel => "gpt-4";

    public AzureOpenAiProvider(HttpClient httpClient, ILogger<AzureOpenAiProvider> logger)
    {
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public async Task<string> GenerateCodeAsync(string apiKey, string prompt, string model)
    {
        // Note: Azure OpenAI requires endpoint configuration
        // This is a simplified implementation - in production, you'd need endpoint configuration per key
        logger.LogError("Azure OpenAI integration requires endpoint configuration. Please implement based on your Azure setup.");
        throw new NotImplementedException("Azure OpenAI integration requires endpoint configuration. Please implement based on your Azure setup.");
    }
}