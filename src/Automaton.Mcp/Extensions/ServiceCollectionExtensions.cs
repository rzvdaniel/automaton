using Microsoft.Extensions.DependencyInjection;

namespace Automaton.Mcp.Client.Extensions;

/// <summary>
/// Service collection extensions for registering MCP client services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Automaton MCP client services to the service collection.
    /// </summary>
    public static IServiceCollection AddAutomatonMcp(this IServiceCollection services)
    {
        services.AddScoped<IAutomatonMcpClient, AutomatonMcpClient>();
        return services;
    }
}
