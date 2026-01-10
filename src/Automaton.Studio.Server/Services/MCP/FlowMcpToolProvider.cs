using System.Text.Json;
using Automaton.Mcp.Models;
using Automaton.Mcp.Services;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// MCP tool provider for Flow editing context
/// </summary>
public class FlowMcpToolProvider : IMcpToolProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string? _flowId;
    private readonly Dictionary<string, object>? _context;

    public FlowMcpToolProvider(
        IServiceProvider serviceProvider,
        string? flowId,
        Dictionary<string, object>? context)
    {
        _serviceProvider = serviceProvider;
        _flowId = flowId;
        _context = context;
    }

    public Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        var tools = new List<McpTool>
        {
            new McpTool
            {
                Name = "search_steps",
                Description = "Search for existing reusable steps",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""query"": {
                            ""type"": ""string"",
                            ""description"": ""Search query to find steps""
                        }
                    },
                    ""required"": [""query""]
                }")
            },
            new McpTool
            {
                Name = "get_flow_definition",
                Description = "Get the current flow structure and steps",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {}
                }")
            }
        };

        return Task.FromResult<IEnumerable<McpTool>>(tools);
    }

    public async Task<McpToolResult> ExecuteToolAsync(
        string toolName,
        JsonElement arguments,
        string? contextId = null,
        Dictionary<string, object>? context = null)
    {
        try
        {
            return toolName switch
            {
                "search_steps" => await SearchSteps(arguments),
                "get_flow_definition" => await GetFlowDefinition(contextId ?? _flowId),
                _ => new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" }
            };
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private async Task<McpToolResult> SearchSteps(JsonElement arguments)
    {
        var query = arguments.GetProperty("query").GetString();

        using var scope = _serviceProvider.CreateScope();
        var stepsService = scope.ServiceProvider.GetRequiredService<CustomStepsService>();

        // Get userId from context
        var userId = _context?.GetValueOrDefault("userId")?.ToString();
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
        {
            return new McpToolResult { Success = false, Error = "User ID is required" };
        }

        var steps = await stepsService.SearchAsync(query!, userGuid);

        if (!steps.Any())
        {
            return new McpToolResult
            {
                Success = true,
                Content = $"No steps found matching '{query}'"
            };
        }

        var stepList = string.Join("\n", steps.Take(10).Select(s =>
            $"- {s.Name} (ID: {s.Id})\n  Display: {s.DisplayName}\n  Language: {s.Language}\n  Description: {s.Description ?? "No description"}"));

        return new McpToolResult
        {
            Success = true,
            Content = $"Found {steps.Count()} steps:\n{stepList}"
        };
    }

    private async Task<McpToolResult> GetFlowDefinition(string? flowId)
    {
        if (string.IsNullOrEmpty(flowId) || !Guid.TryParse(flowId, out var flowGuid))
        {
            return new McpToolResult { Success = false, Error = "Flow ID is required" };
        }

        using var scope = _serviceProvider.CreateScope();
        var flowsService = scope.ServiceProvider.GetRequiredService<FlowsService>();

        var flow = await flowsService.GetAsync(flowGuid);
        if (flow == null)
        {
            return new McpToolResult { Success = false, Error = "Flow not found" };
        }

        return new McpToolResult
        {
            Success = true,
            Content = JsonSerializer.Serialize(new
            {
                id = flow.Id,
                name = flow.Name,
                description = flow.Description,
                definition = flow.Definition
            }, new JsonSerializerOptions { WriteIndented = true })
        };
    }
}

/// <summary>
/// Default MCP tool provider with no tools
/// </summary>
public class DefaultMcpToolProvider : IMcpToolProvider
{
    public Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        return Task.FromResult<IEnumerable<McpTool>>(new List<McpTool>());
    }

    public Task<McpToolResult> ExecuteToolAsync(
        string toolName,
        JsonElement arguments,
        string? contextId = null,
        Dictionary<string, object>? context = null)
    {
        return Task.FromResult(new McpToolResult
        {
            Success = false,
            Error = $"No tools available. Tool '{toolName}' not found."
        });
    }
}
