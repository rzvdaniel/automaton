using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Automaton.Mcp.Client.Providers;

namespace Automaton.Mcp.Client.Extensions;

/// <summary>
/// Service collection extensions for registering MCP client services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Automaton MCP client services to the service collection
    /// </summary>
    public static IServiceCollection AddAutomatonMcpClient(this IServiceCollection services)
    {
        // Register the provider registry
        services.AddSingleton<IAiProviderRegistry>(sp =>
        {
            var registry = new AiProviderRegistry();
            var logger = sp.GetRequiredService<ILoggerFactory>();
            
            // Register OpenAI provider
            var openAiProvider = new OpenAiProvider(logger.CreateLogger("OpenAiProvider"));
            registry.Register(openAiProvider);
            
            // Register Anthropic provider
            var anthropicProvider = new AnthropicProvider(logger.CreateLogger("AnthropicProvider"));
            registry.Register(anthropicProvider);

            // Register Google Gemini provider
            var googleProvider = new GoogleProvider(logger.CreateLogger("GoogleProvider"));
            registry.Register(googleProvider);
            
            return registry;
        });
        
        // Register the MCP client (tool provider is set dynamically via SetToolProvider)
        services.AddScoped<IAutomatonMcpClient, AutomatonMcpClient>();
        
        return services;
    }
}