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

    private readonly IAutomatonGuidanceProvider _guidanceProvider;

    public AutomatonGuidanceTool(IAutomatonGuidanceProvider guidanceProvider)
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
            ListGuidanceName => Task.FromResult(ListGuidance()),
            GetGuidanceName => Task.FromResult(GetGuidance(arguments)),
            _ => Task.FromResult(new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" })
        };
    }

    private McpToolResult ListGuidance()
    {
        var references = _guidanceProvider.GetReferenceSources()
            .Where(source => source.RelativePath.StartsWith("llms/", StringComparison.Ordinal))
            .Select(source => source.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
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

    private McpToolResult GetGuidance(JsonElement arguments)
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

        try
        {
            var content = _guidanceProvider.GetReference(referenceName.Trim());
            return new McpToolResult
            {
                Success = true,
                Content = content
            };
        }
        catch (InvalidOperationException ex)
        {
            return new McpToolResult { Success = false, Error = ex.Message };
        }
    }
}
