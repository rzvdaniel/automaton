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
    private static readonly IReadOnlyDictionary<string, GuidanceCatalogEntry> GuidanceCatalog =
        new Dictionary<string, GuidanceCatalogEntry>(StringComparer.Ordinal)
        {
            ["llms/automaton-app-contract-compact.md"] = new("shared", "contract", "Compact always-on Automaton app contract for Studio editing."),
            ["llms/automaton-studio-editor.md"] = new("studio", "editor", "Studio editor behavior, file editing rules, and verification expectations."),
            ["llms/automaton-backend-controllers.md"] = new("shared", "backend", "Python controller rules, response envelope, and backend structure guidance."),
            ["llms/automaton-media-assets.md"] = new("shared", "media", "Bundled assets versus runtime media, uploads, and public/static asset handling."),
            ["llms/automaton-frontend-structure.md"] = new("shared", "frontend", "Frontend structure, relative URLs, PWA rules, and installable app patterns."),
            ["llms/automaton-starter-prompts.md"] = new("cli", "starter", "Starter app prompts and initial app creation guidance."),
            ["llms/automaton-cli-deploy.md"] = new("cli", "deploy", "Deploy and runtime troubleshooting guidance for Automaton apps."),
            ["llms/automaton-cli-install.md"] = new("cli", "install", "Automaton CLI installation and bootstrap guidance."),
            ["llms/automaton-examples.md"] = new("shared", "examples", "Longer examples and reference app patterns."),
            ["llms/automaton-mcp.md"] = new("mcp", "protocol", "Guidance for MCP-related workflows and integrations.")
        };

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
            .Select(source =>
            {
                var metadata = ResolveCatalogEntry(source.RelativePath);
                return new
                {
                    path = source.RelativePath,
                    surface = metadata.Surface,
                    category = metadata.Category,
                    description = metadata.Description
                };
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

    private static GuidanceCatalogEntry ResolveCatalogEntry(string relativePath) =>
        GuidanceCatalog.TryGetValue(relativePath, out var entry)
            ? entry
            : new GuidanceCatalogEntry("shared", "reference", "Focused Automaton guidance reference.");

    private sealed record GuidanceCatalogEntry(string Surface, string Category, string Description);
}
