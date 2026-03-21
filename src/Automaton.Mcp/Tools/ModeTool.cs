using System.Text.Json;
using Automaton.Mcp.Models;
using Automaton.Mcp.Modes;

namespace Automaton.Mcp.Tools;

/// <summary>
/// Allows the AI to explicitly switch between agent and plan modes.
/// The <c>switchedTo</c> field in the tool result is read by the frontend,
/// which immediately updates the chat mode — no re-send, no loop halt.
/// </summary>
public sealed class ModeTool : IMcpTool
{
    public const string SwitchModeName = "switch_mode";

    private static readonly HashSet<string> HandledTools = [SwitchModeName];

    public Task<IEnumerable<McpTool>> GetToolsAsync() =>
        Task.FromResult<IEnumerable<McpTool>>(
        [
            new McpTool
            {
                Name        = SwitchModeName,
                Capability  = ToolCapability.ModeSwitch,
                Description = """
                    Switch the conversation to a different mode.
                    Call switch_mode(to: "agent") after propose_plan — signals the client to switch to agent mode so the user can confirm implementation.
                    Call switch_mode(to: "plan") from agent mode when a task is complex enough to warrant planning first.
                    After calling this tool, write a short plain-text message explaining what the user should do next.
                    """,
                InputSchema = JsonDocument.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "to": {
                                "type": "string",
                                "enum": ["agent", "plan"],
                                "description": "Target mode: 'agent' for implementation, 'plan' for planning"
                            },
                            "reason": {
                                "type": "string",
                                "description": "Optional brief reason shown to the user"
                            }
                        },
                        "required": ["to"]
                    }
                    """)
            }
        ]);

    public bool CanHandle(string toolName) => HandledTools.Contains(toolName);

    public Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, Dictionary<string, object>? context)
    {
        if (!CanHandle(toolName))
            return Task.FromResult(new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" });

        if (!arguments.TryGetProperty("to", out var toProp) || toProp.GetString() is not string to)
            return Task.FromResult(new McpToolResult { Success = false, Error = "Missing required parameter: 'to'" });

        if (to != ConversationModes.Agent && to != ConversationModes.Plan)
            return Task.FromResult(new McpToolResult { Success = false, Error = $"Invalid mode '{to}'. Must be 'agent' or 'plan'." });

        var result = JsonSerializer.Serialize(new { switchedTo = to });
        return Task.FromResult(new McpToolResult { Success = true, Content = result });
    }
}
