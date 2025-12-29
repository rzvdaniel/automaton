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
}