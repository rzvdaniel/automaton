using System.Text.Json;
using Automaton.AiGuidance;
using Automaton.Mcp.Models;

namespace Automaton.Mcp.Tools;

/// <summary>
/// Read-only MCP tools for listing and retrieving focused Automaton guidance references.
/// </summary>
public sealed class AutomatonGuidanceTool : IMcpTool
{
    public const string ListGuidanceName = "list_automaton_guidance";
    public const string GetGuidanceName = "get_automaton_guidance";

    private static readonly HashSet<string> HandledTools = [ListGuidanceName, GetGuidanceName];
    private readonly IAutomatonGuidanceRuntimeProvider _guidanceProvider;

    public AutomatonGuidanceTool(IAutomatonGuidanceRuntimeProvider guidanceProvider)
    {
        _guidanceProvider = guidanceProvider;
    }

    public Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        var tools = new List<McpTool>
        {
            new()
            {
                Name = ListGuidanceName,
                Capability = ToolCapability.Read,
                Description = "List focused Automaton guidance references available for Studio AI.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""")
            },
            new()
            {
                Name = GetGuidanceName,
                Capability = ToolCapability.Read,
                Description = "Retrieve a focused Automaton guidance reference by key returned from list_automaton_guidance.",
                InputSchema = JsonDocument.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "key": {
                                "type": "string",
                                "description": "A guidance key returned by list_automaton_guidance."
                            }
                        },
                        "required": ["key"]
                    }
                    """)
            }
        };

        return Task.FromResult<IEnumerable<McpTool>>(tools);
    }

    public bool CanHandle(string toolName) => HandledTools.Contains(toolName);

    public Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, Dictionary<string, object>? context)
    {
        return toolName switch
        {
            ListGuidanceName => ListGuidanceAsync(),
            GetGuidanceName => GetGuidanceAsync(arguments),
            _ => Task.FromResult(new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" })
        };
    }

    private async Task<McpToolResult> ListGuidanceAsync()
    {
        var references = (await _guidanceProvider.ListLoadableGuidanceAsync())
            .Select(source => new
            {
                key = source.Key,
                title = source.Title,
                surface = source.Surface,
                documentKind = source.DocumentKind,
                tagsJson = source.TagsJson,
                description = source.Description,
                requiredByDefault = source.RequiredByDefault,
                isModelLoadable = source.IsModelLoadable,
                sortOrder = source.SortOrder
            })
            .OrderBy(entry => entry.key, StringComparer.Ordinal)
            .ToArray();

        return new McpToolResult
        {
            Success = true,
            Content = JsonSerializer.Serialize(new
            {
                references
            })
        };
    }

    private async Task<McpToolResult> GetGuidanceAsync(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("key", out var referenceElement))
        {
            return new McpToolResult { Success = false, Error = "Missing required argument 'key'." };
        }

        var key = referenceElement.GetString();
        if (string.IsNullOrWhiteSpace(key))
        {
            return new McpToolResult { Success = false, Error = "Argument 'key' must be a non-empty string." };
        }

        var document = await _guidanceProvider.GetGuidanceByKeyAsync(key.Trim());
        if (document is null)
        {
            return new McpToolResult { Success = false, Error = $"Automaton guidance reference '{key.Trim()}' was not found." };
        }

        return new McpToolResult
        {
            Success = true,
            Content = document.ContentMarkdown
        };
    }
}
