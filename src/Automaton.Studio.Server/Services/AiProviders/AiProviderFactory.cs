using Automaton.Studio.Server.Entities.Enums;

namespace Automaton.Studio.Server.Services.AiProviders;

public class AiProviderFactory
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<AiProviderFactory> logger;

    public AiProviderFactory(IServiceProvider serviceProvider, ILogger<AiProviderFactory> logger)
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
    }

    public IAiProvider CreateProvider(AiProviderType providerType)
    {
        return providerType switch
        {
            AiProviderType.OpenAI => serviceProvider.GetRequiredService<OpenAiProvider>(),
            AiProviderType.Anthropic => serviceProvider.GetRequiredService<AnthropicProvider>(),
            AiProviderType.Google => serviceProvider.GetRequiredService<GoogleProvider>(),
            _ => throw new NotSupportedException($"Provider {providerType} is not supported yet.")
        };
    }

    public IReadOnlyList<string> GetSupportedModels(AiProviderType providerType)
    {
        var provider = CreateProvider(providerType);
        return provider.SupportedModels;
    }

    public string GetDefaultModel(AiProviderType providerType)
    {
        var provider = CreateProvider(providerType);
        return provider.DefaultModel;
    }
}