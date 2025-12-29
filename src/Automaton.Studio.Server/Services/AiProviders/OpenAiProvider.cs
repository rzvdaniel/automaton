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
}