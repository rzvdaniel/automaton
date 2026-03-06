using System.Text;
using System.Text.Json;
using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Mcp.Tools;

/// <summary>
/// MCP tool group for AI planning: propose_plan.
/// Resource-agnostic — works for apps, steps, flows, or anything future.
/// Writes plan files to .plans/ subfolder within the scoped file system root.
/// Returns structured JSON so the frontend can render an interactive approval card.
/// </summary>
public class PlanningTool : IMcpTool
{
    private static readonly HashSet<string> HandledTools = ["propose_plan"];

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
                Name = "propose_plan",
                Description = """
                    Propose a design or implementation plan before starting work.
                    Use planType="design" to present the architecture/component breakdown for user approval.
                    Use planType="implementation" to present a staged build plan for user approval.
                    Use planType="stage_complete" to mark a stage as done and ask whether to continue.
                    The plan is saved as a markdown file and the user must approve before implementation begins.
                    IMPORTANT: After calling this tool, present a brief summary to the user and STOP.
                    Do not write any code or files until the user explicitly approves.
                    """,
                InputSchema = JsonDocument.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "planType": {
                                "type": "string",
                                "enum": ["design", "implementation", "stage_complete"],
                                "description": "Type of plan: design (architecture), implementation (staged build), or stage_complete (mark stage done)"
                            },
                            "title": { "type": "string", "description": "Short title for the plan" },
                            "summary": { "type": "string", "description": "1-3 sentence overview" },
                            "components": {
                                "type": "array",
                                "description": "For design plans: list of components/modules",
                                "items": {
                                    "type": "object",
                                    "properties": {
                                        "name": { "type": "string" },
                                        "responsibility": { "type": "string" }
                                    },
                                    "required": ["name", "responsibility"]
                                }
                            },
                            "techDecisions": {
                                "type": "array",
                                "description": "For design plans: key technology/approach decisions",
                                "items": {
                                    "type": "object",
                                    "properties": {
                                        "decision": { "type": "string" },
                                        "rationale": { "type": "string" }
                                    },
                                    "required": ["decision", "rationale"]
                                }
                            },
                            "stages": {
                                "type": "array",
                                "description": "For implementation plans: ordered list of build stages",
                                "items": {
                                    "type": "object",
                                    "properties": {
                                        "index":               { "type": "integer" },
                                        "name":                { "type": "string" },
                                        "description":         { "type": "string" },
                                        "files":               { "type": "array", "items": { "type": "string" } },
                                        "estimatedComplexity": { "type": "string", "enum": ["low", "medium", "high"] }
                                    },
                                    "required": ["index", "name", "description"]
                                }
                            },
                            "stageIndex":   { "type": "integer", "description": "For stage_complete: which stage just finished (0-based)" },
                            "stageSummary": { "type": "string",  "description": "For stage_complete: what was done" },
                            "filesChanged": { "type": "array", "items": { "type": "string" }, "description": "For stage_complete: files created or modified" }
                        },
                        "required": ["planType", "title", "summary"]
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
            "propose_plan" => await ProposePlan(arguments, context),
            _ => new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" }
        };
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private async Task<McpToolResult> ProposePlan(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var planType    = arguments.GetProperty("planType").GetString()!;
            var title       = arguments.GetProperty("title").GetString()!;
            var summary     = arguments.GetProperty("summary").GetString()!;

            var fs = _fileSystemFactory.Create(arguments, context);

            string planFilePath;
            string markdownContent;

            switch (planType)
            {
                case "design":
                    planFilePath = ".plans/DESIGN.md";
                    // Guard: reject if design plan already exists (model skipped the read_file check).
                    if (await fs.ReadFileAsync(planFilePath) != null)
                    {
                        return new McpToolResult
                        {
                            Success = false,
                            Error = "Design plan already exists at .plans/DESIGN.md. " +
                                    "Read it with read_file(\".plans/IMPLEMENTATION.md\") to check if an implementation plan exists, " +
                                    "then proceed to Step 2 of the planning gate."
                        };
                    }
                    markdownContent = BuildDesignMarkdown(title, summary, arguments);
                    break;

                case "implementation":
                    planFilePath = ".plans/IMPLEMENTATION.md";
                    // Guard: reject if implementation plan already exists (model skipped the read_file check).
                    if (await fs.ReadFileAsync(planFilePath) != null)
                    {
                        return new McpToolResult
                        {
                            Success = false,
                            Error = "Implementation plan already exists at .plans/IMPLEMENTATION.md. " +
                                    "Do NOT call propose_plan again. Proceed directly to implementation: " +
                                    "implement ALL stages now, one after another, calling stage_complete after each."
                        };
                    }
                    markdownContent = BuildImplementationMarkdown(title, summary, arguments);
                    break;

                case "stage_complete":
                    planFilePath    = ".plans/IMPLEMENTATION.md";
                    markdownContent = await UpdateStageCompleteAsync(fs, arguments);
                    planFilePath    = string.Empty; // content handled inside
                    break;

                default:
                    return new McpToolResult { Success = false, Error = $"Unknown planType: {planType}" };
            }

            // Write plan file (skip for stage_complete — already handled above)
            if (!string.IsNullOrEmpty(planFilePath))
            {
                await fs.WriteFileAsync(planFilePath, markdownContent);
                _logger.LogInformation("Wrote {PlanType} plan to {Path}", planType, planFilePath);
            }

            // Return structured result for frontend approval card
            var nextStageInfo = planType == "stage_complete"
                ? FindNextStage(markdownContent)
                : null;

            var result = new
            {
                awaitingApproval = planType != "stage_complete",
                planType,
                title,
                summary,
                planFile = string.IsNullOrEmpty(planFilePath) ? ".plans/IMPLEMENTATION.md" : planFilePath,
                components    = GetArrayOrNull(arguments, "components"),
                techDecisions = GetArrayOrNull(arguments, "techDecisions"),
                stages        = GetArrayOrNull(arguments, "stages"),
                stageIndex    = planType == "stage_complete" && arguments.TryGetProperty("stageIndex", out var si) ? si.GetInt32() : (int?)null,
                stageSummary  = planType == "stage_complete" && arguments.TryGetProperty("stageSummary", out var ss) ? ss.GetString() : null,
                filesChanged  = planType == "stage_complete" ? GetStringArray(arguments, "filesChanged") : null,
                // True when more unchecked stages remain — frontend uses this to auto-continue.
                hasMoreStages = nextStageInfo.HasValue,
                // Next stage details for a precise auto-continue message.
                nextStage = nextStageInfo.HasValue
                    ? new { index = nextStageInfo.Value.Index, name = nextStageInfo.Value.Name, description = nextStageInfo.Value.Description }
                    : null,
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing propose_plan");
            return new McpToolResult { Success = false, Error = $"Failed to propose plan: {ex.Message}" };
        }
    }

    // ── Markdown builders ─────────────────────────────────────────────────────

    private static string BuildDesignMarkdown(string title, string summary, JsonElement args)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Design Plan: {title}");
        sb.AppendLine();
        sb.AppendLine($"> {summary}");
        sb.AppendLine();

        if (args.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine("## Components");
            sb.AppendLine();
            foreach (var c in components.EnumerateArray())
            {
                var name = c.TryGetProperty("name", out var n) ? n.GetString() : "Component";
                var resp = c.TryGetProperty("responsibility", out var r) ? r.GetString() : "";
                sb.AppendLine($"- **{name}** — {resp}");
            }
            sb.AppendLine();
        }

        if (args.TryGetProperty("techDecisions", out var decisions) && decisions.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine("## Technical Decisions");
            sb.AppendLine();
            foreach (var d in decisions.EnumerateArray())
            {
                var decision  = d.TryGetProperty("decision", out var dec) ? dec.GetString() : "";
                var rationale = d.TryGetProperty("rationale", out var rat) ? rat.GetString() : "";
                sb.AppendLine($"- **{decision}** — {rationale}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"_Created by AI on {DateTime.UtcNow:yyyy-MM-dd}_");
        return sb.ToString();
    }

    private static string BuildImplementationMarkdown(string title, string summary, JsonElement args)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Implementation Plan: {title}");
        sb.AppendLine();
        sb.AppendLine($"> {summary}");
        sb.AppendLine();
        sb.AppendLine("## Stages");
        sb.AppendLine();

        if (args.TryGetProperty("stages", out var stages) && stages.ValueKind == JsonValueKind.Array)
        {
            foreach (var stage in stages.EnumerateArray())
            {
                var idx  = stage.TryGetProperty("index", out var i) ? i.GetInt32() : 0;
                var name = stage.TryGetProperty("name", out var n) ? n.GetString() : "Stage";
                var desc = stage.TryGetProperty("description", out var d) ? d.GetString() : "";
                var comp = stage.TryGetProperty("estimatedComplexity", out var c) ? c.GetString() : "medium";

                sb.AppendLine($"- [ ] Stage {idx + 1} · {name} _(complexity: {comp})_");
                if (!string.IsNullOrEmpty(desc))
                    sb.AppendLine($"  > {desc}");

                if (stage.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                {
                    var fileList = files.EnumerateArray().Select(f => f.GetString()).Where(f => f != null);
                    var joined = string.Join(", ", fileList);
                    if (!string.IsNullOrEmpty(joined))
                        sb.AppendLine($"  > Files: `{joined}`");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine($"_Created by AI on {DateTime.UtcNow:yyyy-MM-dd}_");
        return sb.ToString();
    }

    private static async Task<string> UpdateStageCompleteAsync(IScopedFileSystem fs, JsonElement args)
    {
        var stageIndex = args.TryGetProperty("stageIndex", out var si) ? si.GetInt32() : -1;
        var existing   = await fs.ReadFileAsync(".plans/IMPLEMENTATION.md");

        if (existing is null || stageIndex < 0)
            return existing ?? string.Empty;

        // Replace "- [ ] Stage {stageIndex+1}" with "- [x]" and append completion note
        var marker  = $"- [ ] Stage {stageIndex + 1} ·";
        var replace = $"- [x] Stage {stageIndex + 1} · ";

        if (!existing.Contains(marker))
            return existing;

        var updated = existing.Replace(marker, replace, StringComparison.Ordinal);

        // Append a completion log at the bottom
        var summary = args.TryGetProperty("stageSummary", out var ss) ? ss.GetString() : null;
        if (!string.IsNullOrEmpty(summary))
        {
            updated += $"\n---\n_Stage {stageIndex + 1} completed on {DateTime.UtcNow:yyyy-MM-dd}: {summary}_\n";
        }

        await fs.WriteFileAsync(".plans/IMPLEMENTATION.md", updated);
        return updated;
    }

    /// <summary>
    /// Parse the first unchecked stage from the updated markdown.
    /// Returns (name, description, index) or null if all stages are done.
    /// </summary>
    private static (string Name, string Description, int Index)? FindNextStage(string markdown)
    {
        // Lines look like: "- [ ] Stage N · StageName _(complexity: low)_"
        // followed optionally by "  > description"
        var lines = markdown.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();
            if (!line.StartsWith("- [ ] Stage ")) continue;

            // Extract stage number: "Stage N · ..."
            var afterStage = line["- [ ] Stage ".Length..];
            var dotPos = afterStage.IndexOf(" · ");
            if (dotPos < 0) continue;

            if (!int.TryParse(afterStage[..dotPos].Trim(), out var stageNum)) continue;
            var zeroBasedIndex = stageNum - 1;

            // Extract name: between " · " and " _(complexity"
            var afterDot = afterStage[(dotPos + 3)..];
            var complexityPos = afterDot.IndexOf(" _(complexity");
            var name = complexityPos >= 0 ? afterDot[..complexityPos].Trim() : afterDot.Trim();

            // Look for description on the next line
            var desc = string.Empty;
            if (i + 1 < lines.Length)
            {
                var nextLine = lines[i + 1].TrimStart();
                if (nextLine.StartsWith("> "))
                    desc = nextLine[2..].Trim();
            }

            return (name, desc, zeroBasedIndex);
        }
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object? GetArrayOrNull(JsonElement args, string property)
    {
        if (!args.TryGetProperty(property, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return null;

        return JsonSerializer.Deserialize<object[]>(prop.GetRawText());
    }

    private static string[]? GetStringArray(JsonElement args, string property)
    {
        if (!args.TryGetProperty(property, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return null;

        return prop.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => s != null)
            .ToArray()!;
    }
}
