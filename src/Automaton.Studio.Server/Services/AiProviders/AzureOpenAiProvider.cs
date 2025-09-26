using Automaton.Studio.Server.Config;
using Automaton.Studio.Server.Entities;
using Microsoft.Extensions.Options;

namespace Automaton.Studio.Server.Services.AiProviders;

public class AzureOpenAiProvider : IAiProvider
{
    private readonly HttpClient httpClient;
    private readonly ILogger<AzureOpenAiProvider> logger;
    private readonly AiProvidersConfig aiProvidersConfig;

    public AiProvider ProviderType => AiProvider.Azure;

    private static readonly List<string> DefaultSupportedModels = new()
    {
        "gpt-4o",                    // Latest GPT-4o (if available in Azure)
        "gpt-4o-mini",               // GPT-4o mini (if available in Azure)
        "gpt-4-turbo",               // GPT-4 Turbo
        "gpt-4",                     // Standard GPT-4
        "gpt-35-turbo",              // Azure naming for GPT-3.5-turbo
        "gpt-35-turbo-16k"           // GPT-3.5-turbo with 16k context
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

    public AzureOpenAiProvider(HttpClient httpClient, ILogger<AzureOpenAiProvider> logger, IOptions<AiProvidersConfig> aiProvidersConfig)
    {
        this.httpClient = httpClient;
        this.logger = logger;
        this.aiProvidersConfig = aiProvidersConfig.Value;
    }

    public async Task<string> GenerateCodeAsync(string apiKey, string prompt, string model)
    {
        // Note: Azure OpenAI requires endpoint configuration
        // This is a simplified implementation - in production, you'd need endpoint configuration per key
        logger.LogError("Azure OpenAI integration requires endpoint configuration. Please implement based on your Azure setup.");
        throw new NotImplementedException("Azure OpenAI integration requires endpoint configuration. Please implement based on your Azure setup.");
    }
}