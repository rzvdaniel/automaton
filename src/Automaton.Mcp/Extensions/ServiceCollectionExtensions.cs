using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Automaton.Mcp.Client.Providers;

namespace Automaton.Mcp.Client.Extensions;

/// <summary>
/// Service collection extensions for registering MCP client services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Automaton MCP client services to the service collection.
    /// <para>
    /// Registers <see cref="AiProviderRegistry"/> under both its concrete type and the
    /// <see cref="IAiProviderRegistry"/> interface so host applications can replace the
    /// interface registration with a decorator (e.g., a DB-backed dynamic registry) while
    /// still injecting the concrete registry to seed it with hardcoded providers.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAutomatonMcpClient(this IServiceCollection services)
    {
        // Register the concrete registry so host applications can inject it as a dependency
        // in decorators without creating a second instance.
        services.AddSingleton<AiProviderRegistry>(sp =>
        {
            var registry = new AiProviderRegistry();
            var logger = sp.GetRequiredService<ILoggerFactory>();
            var configuration = sp.GetService<IConfiguration>();
            var maxConversationHistoryMessages =
                configuration?.GetValue<int?>("AiProviders:MaxConversationHistoryMessages") ?? 30;
            
            // Register OpenAI provider
            var openAiProvider = new OpenAiProvider(logger.CreateLogger("OpenAiProvider"));
            registry.Register(openAiProvider);
            
            // Register Anthropic provider
            var anthropicProvider = new AnthropicProvider(logger.CreateLogger("AnthropicProvider"));
            registry.Register(anthropicProvider);

            // Register Google Gemini provider
            var googleProvider = new GoogleProvider(logger.CreateLogger("GoogleProvider"));
            registry.Register(googleProvider);

            // Register Moonshot (Kimi) — OpenAI Chat Completions-compatible
            // Uses the international endpoint. Keys from platform.moonshot.ai authenticate here.
            var moonshotProvider = new OpenAiCompatibleProvider(
                providerName: "Moonshot",
                baseUrl: "https://api.moonshot.ai/v1",
                defaultModel: "kimi-k2-5",
                logger: logger.CreateLogger("MoonshotProvider"),
                maxConversationHistoryMessages: maxConversationHistoryMessages);
            registry.Register(moonshotProvider);
            
            return registry;
        });

        // Default interface binding — host applications may replace this with a decorator.
        services.AddSingleton<IAiProviderRegistry>(sp => sp.GetRequiredService<AiProviderRegistry>());
        
        // Register the MCP client (tool provider is set dynamically via SetToolProvider)
        services.AddScoped<IAutomatonMcpClient, AutomatonMcpClient>();
        
        return services;
    }
}