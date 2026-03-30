using System.Text.Json;
using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Mcp.Tools;

/// <summary>
/// MCP tool for AI planning: propose_plan.
/// Writes a plan file and sets awaitingApproval so the frontend renders an approval card.
/// </summary>
public class PlanningTool : IMcpTool
{
    public const string ProposePlanName = "propose_plan";

    private static readonly HashSet<string> HandledTools = [ProposePlanName];

    private readonly IScopedFileSystemFactory _fileSystemFactory;
    private readonly ILogger<PlanningTool> _logger;

    public PlanningTool(IScopedFileSystemFactory fileSystemFactory, ILogger<PlanningTool> logger)
    {
        _fileSystemFactory = fileSystemFactory;
        _logger = logger;
    }

    public Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        var tools = new List<McpTool>
        {
            new McpTool
            {
                Name = ProposePlanName,
                Capability = ToolCapability.Planning,
                Description = """
                    Propose a plan before implementing a new app or significant feature.
                    Presents the plan for user approval before writing any files.
                    After calling, render the plan as markdown and ask whether to proceed. Do not call any other tools.
                    """,
                InputSchema = JsonDocument.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "title":   { "type": "string", "description": "Short title for the plan" },
                            "summary": { "type": "string", "description": "What will be built and how — enough detail that the user can approve or reject." }
                        },
                        "required": ["title", "summary"]
                    }
                    """)
            }
        };

        return Task.FromResult<IEnumerable<McpTool>>(tools);
    }

    public bool CanHandle(string toolName) => HandledTools.Contains(toolName);

    public async Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, Dictionary<string, object>? context)
    {
        return toolName switch
        {
            ProposePlanName => await ProposePlan(arguments, context),
            _ => new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" }
        };
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private async Task<McpToolResult> ProposePlan(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var title   = arguments.GetProperty("title").GetString()!;
            var summary = arguments.GetProperty("summary").GetString()!;

            var fs = _fileSystemFactory.Create(arguments, context);
            const string planFilePath = ".plans/IMPLEMENTATION.md";

            var existingPlan = await fs.ReadFileAsync(planFilePath);
            var isRefinement = existingPlan != null;

            // If a plan already exists redirect to implementation.
            if (isRefinement)
            {
                return new McpToolResult
                {
                    Success = true,
                    Content = "{\"instruction\":\"Plan already exists. Do NOT call propose_plan again. Proceed with implementation using write_file/edit_file, or if implementation is already complete, STOP and write a summary.\",\"alreadyExists\":true}"
                };
            }

            var markdown = $"# Plan: {title}\n\n> {summary}\n\n_Created by AI on {DateTime.UtcNow:yyyy-MM-dd}_\n";
            await fs.WriteFileAsync(planFilePath, markdown);
            _logger.LogInformation("Wrote implementation plan to {Path}", planFilePath);

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(new
                {
                    instruction = "Plan saved. Render the plan as markdown and ask whether to proceed. Do NOT call any other tools until confirmed.",
                    awaitingApproval = true,
                    planFile = planFilePath
                })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing propose_plan");
            return new McpToolResult { Success = false, Error = $"Failed to propose plan: {ex.Message}" };
        }
    }
}
