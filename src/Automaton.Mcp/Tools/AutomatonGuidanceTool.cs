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
                Description = "List focused Automaton guidance references available for Studio AI. Use this before guessing about Automaton-specific structure, backend controllers, media, PWA, deploy, or starter patterns.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""")
            },
            new()
            {
                Name = GetGuidanceName,
                Capability = ToolCapability.Read,
                Description = "Retrieve a focused Automaton guidance reference by relative path, such as 'llms/automaton-backend-controllers.md' or 'llms/automaton-media-assets.md'. Use this when a task needs deeper Automaton-specific rules than the compact baseline prompt provides.",
                InputSchema = JsonDocument.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "referenceName": {
                                "type": "string",
                                "description": "Relative guidance path returned by list_automaton_guidance, for example 'llms/automaton-backend-controllers.md'."
                            }
                        },
                        "required": ["referenceName"]
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
        var references = (await _guidanceProvider.ListReferencesAsync())
            .Select(source => new
            {
                path = source.RelativePath,
                surface = source.Surface,
                tagsJson = source.TagsJson,
                description = source.Description
            })
            .OrderBy(entry => entry.path, StringComparer.Ordinal)
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
        if (!arguments.TryGetProperty("referenceName", out var referenceElement))
        {
            return new McpToolResult { Success = false, Error = "Missing required argument 'referenceName'." };
        }

        var referenceName = referenceElement.GetString();
        if (string.IsNullOrWhiteSpace(referenceName))
        {
            return new McpToolResult { Success = false, Error = "Argument 'referenceName' must be a non-empty string." };
        }

        var document = await _guidanceProvider.GetReferenceAsync(referenceName.Trim());
        if (document is null)
        {
            return new McpToolResult { Success = false, Error = $"Automaton guidance reference '{referenceName.Trim()}' was not found." };
        }

        return new McpToolResult
        {
            Success = true,
            Content = document.ContentMarkdown
        };
    }
}
