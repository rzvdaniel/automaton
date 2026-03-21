using System.Text.Json;
using Automaton.Mcp.Models;

namespace Automaton.Mcp.Tools;

/// <summary>
/// Provides the <c>assess_task</c> tool, which forces the AI to explicitly reason
/// about task complexity before taking any action. The reasoning is visible in the
/// tool call history and server-side enforcement halts the loop when a plan is recommended.
/// </summary>
public class AssessmentTool : IMcpTool
{
    public const string AssessTaskName = "assess_task";

    private static readonly HashSet<string> HandledTools = [AssessTaskName];

    private static readonly McpTool AssessTaskDefinition = new()
    {
        Name        = AssessTaskName,
        Capability  = ToolCapability.Assessment,
        Description =
            """
            MANDATORY: Call this as your FIRST action before reading or writing any file.
            Assess the user's request and decide whether to proceed directly or propose a plan first.

            - Use "proceed" for: bug fixes, style tweaks, copy changes, single-function edits, anything obviously small and reversible.
            - Use "plan" for: new features, new files, backend + frontend changes together, database additions, architectural decisions, or anything the user describes as a "revamp" or major change.

            When recommendation is "plan", the system will automatically switch to plan mode — do NOT call request_plan separately.
            When recommendation is "proceed", continue with your implementation immediately after this call.
            """,
        InputSchema = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": {
                "summary":        { "type": "string", "description": "One sentence describing what the user is asking to do." },
                "complexity":     { "type": "string", "enum": ["low", "medium", "high"], "description": "Your complexity assessment." },
                "reasons":        { "type": "array",  "items": { "type": "string" }, "description": "Key reasons for your complexity assessment (1–4 bullet points)." },
                "recommendation": { "type": "string", "enum": ["proceed", "plan"], "description": "Whether to proceed directly or propose a plan first." }
              },
              "required": ["summary", "complexity", "reasons", "recommendation"]
            }
            """)
    };

    public Task<IEnumerable<McpTool>> GetToolsAsync() =>
        Task.FromResult<IEnumerable<McpTool>>([AssessTaskDefinition]);

    public bool CanHandle(string toolName) => HandledTools.Contains(toolName);

    public Task<McpToolResult> ExecuteToolAsync(
        string toolName,
        JsonElement arguments,
        Dictionary<string, object>? context)
    {
        if (toolName != AssessTaskName)
            return Task.FromResult(new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" });

        var summary        = arguments.TryGetProperty("summary",        out var s) ? s.GetString() ?? "" : "";
        var complexity     = arguments.TryGetProperty("complexity",     out var c) ? c.GetString() ?? "low" : "low";
        var recommendation = arguments.TryGetProperty("recommendation", out var r) ? r.GetString() ?? "proceed" : "proceed";

        var reasons = arguments.TryGetProperty("reasons", out var ra) && ra.ValueKind == JsonValueKind.Array
            ? ra.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
            : [];

        // When plan is recommended, return requestPlan:true so the frontend switches to plan mode
        // and PlanGateTracker halts the tool loop — same mechanism as request_plan.
        var content = recommendation == "plan"
            ? JsonSerializer.Serialize(new
            {
                requestPlan = true,
                assessment  = new { summary, complexity, reasons }
            })
            : JsonSerializer.Serialize(new
            {
                proceed    = true,
                assessment = new { summary, complexity, reasons }
            });

        return Task.FromResult(new McpToolResult { Success = true, Content = content });
    }
}
