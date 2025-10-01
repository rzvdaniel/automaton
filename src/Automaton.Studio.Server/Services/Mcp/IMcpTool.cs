using System.Text.Json;

namespace Automaton.Studio.Server.Services.Mcp;

/// <summary>
/// Interface for MCP (Model Context Protocol) tools that can be called by AI models
/// </summary>
public interface IMcpTool
{
    /// <summary>
    /// Unique name of the tool
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Description of what the tool does
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// JSON schema for the tool parameters
    /// </summary>
    JsonDocument ParametersSchema { get; }
    
    /// <summary>
    /// Execute the tool with the given parameters
    /// </summary>
    /// <param name="parameters">Parameters as a JSON string</param>
    /// <param name="userId">User ID for authorization</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tool execution result as a string</returns>
    Task<string> ExecuteAsync(string parameters, Guid userId, CancellationToken cancellationToken = default);
}
