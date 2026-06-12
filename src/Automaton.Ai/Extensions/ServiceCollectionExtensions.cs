using Automaton.Ai.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Automaton.Ai.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAutomatonAi(this IServiceCollection services)
    {
        services.AddSingleton<AiProviderRegistry>(sp =>
        {
            var registry = new AiProviderRegistry();
            var logger = sp.GetRequiredService<ILoggerFactory>();
            var configuration = sp.GetService<IConfiguration>();
            var maxConversationHistoryMessages =
                configuration?.GetValue<int?>("AiProviders:MaxConversationHistoryMessages") ?? 30;

            registry.Register(new OpenAiProvider(logger.CreateLogger("OpenAiProvider")));
            registry.Register(new AnthropicProvider(logger.CreateLogger("AnthropicProvider")));
            registry.Register(new GoogleProvider(logger.CreateLogger("GoogleProvider")));
            registry.Register(new OpenAiCompatibleProvider(
                providerName: "Moonshot",
                baseUrl: "https://api.moonshot.ai/v1",
                defaultModel: "kimi-k2-5",
                logger: logger.CreateLogger("MoonshotProvider"),
                maxConversationHistoryMessages: maxConversationHistoryMessages));

            return registry;
        });

        services.AddSingleton<IAiProviderRegistry>(sp => sp.GetRequiredService<AiProviderRegistry>());

        return services;
    }
}
