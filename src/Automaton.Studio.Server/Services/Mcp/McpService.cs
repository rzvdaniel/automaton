namespace Automaton.Studio.Server.Services.Mcp;

/// <summary>
/// Service for managing MCP tools and their execution
/// </summary>
public class McpService
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<McpService> logger;
    private readonly Dictionary<string, Type> registeredTools = new();

    public McpService(IServiceProvider serviceProvider, ILogger<McpService> logger)
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
    }

    /// <summary>
    /// Register a tool type
    /// </summary>
    public void RegisterTool<T>() where T : IMcpTool
    {
        // Store the type without creating an instance during registration
        var toolType = typeof(T);
        var toolName = GetToolName(toolType);
        registeredTools[toolName] = toolType;
        logger.LogInformation("Registered MCP tool: {ToolName}", toolName);
    }

    /// <summary>
    /// Get the tool name by creating a temporary instance with a service scope
    /// </summary>
    private string GetToolName(Type toolType)
    {
        using var scope = serviceProvider.CreateScope();
        var tool = (IMcpTool)ActivatorUtilities.CreateInstance(scope.ServiceProvider, toolType);
        return tool.Name;
    }

    /// <summary>
    /// Get all registered tool definitions for AI model
    /// </summary>
    public List<McpToolDefinition> GetToolDefinitions()
    {
        var definitions = new List<McpToolDefinition>();
        
        using var scope = serviceProvider.CreateScope();
        
        foreach (var toolType in registeredTools.Values)
        {
            var tool = (IMcpTool)ActivatorUtilities.CreateInstance(scope.ServiceProvider, toolType);
            definitions.Add(new McpToolDefinition
            {
                Type = "function",
                Function = new McpFunctionDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = System.Text.Json.JsonSerializer.Deserialize<object>(tool.ParametersSchema.RootElement.GetRawText()) ?? new object()
                }
            });
        }
        
        return definitions;
    }

    /// <summary>
    /// Execute a tool by name
    /// </summary>
    public async Task<McpToolResult> ExecuteToolAsync(string toolName, string arguments, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!registeredTools.TryGetValue(toolName, out var toolType))
            {
                logger.LogWarning("Tool not found: {ToolName}", toolName);
                return new McpToolResult
                {
                    IsSuccess = false,
                    ErrorMessage = $"Tool '{toolName}' not found"
                };
            }

            using var scope = serviceProvider.CreateScope();
            var tool = (IMcpTool)ActivatorUtilities.CreateInstance(scope.ServiceProvider, toolType);
            var result = await tool.ExecuteAsync(arguments, userId, cancellationToken);

            return new McpToolResult
            {
                Result = result,
                IsSuccess = true
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return new McpToolResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
