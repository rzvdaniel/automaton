using Automaton.Studio.Server.Entities;

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

    public IAiProvider CreateProvider(AiProvider providerType)
    {
        return providerType switch
        {
            AiProvider.OpenAI => serviceProvider.GetRequiredService<OpenAiProvider>(),
            AiProvider.Anthropic => serviceProvider.GetRequiredService<AnthropicProvider>(),
            AiProvider.Google => serviceProvider.GetRequiredService<GoogleProvider>(),
            AiProvider.Azure => serviceProvider.GetRequiredService<AzureOpenAiProvider>(),
            _ => throw new NotSupportedException($"Provider {providerType} is not supported yet.")
        };
    }

    public IReadOnlyList<string> GetSupportedModels(AiProvider providerType)
    {
        var provider = CreateProvider(providerType);
        return provider.SupportedModels;
    }

    public string GetDefaultModel(AiProvider providerType)
    {
        var provider = CreateProvider(providerType);
        return provider.DefaultModel;
    }
}