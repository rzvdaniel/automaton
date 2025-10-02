using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        // Register the MCP client directly
        services.AddScoped<IAutomatonMcpClient, AutomatonMcpClient>();
        
        return services;
    }
}