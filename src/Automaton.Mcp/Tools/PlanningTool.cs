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
    public const string ProposePlanName  = "propose_plan";

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
                    Call with planType="implementation" to present the full plan for user approval before writing any files.
                    Call with planType="stage_complete" AFTER finishing every stage — this marks it done in the plan file and returns the instruction for what to do next (auto-proceed or pause for approval). You MUST call this between every stage.

                    Stage count guidelines (be conservative):
                    - Small app (< 10 files, self-contained): 1–2 stages max, or a single "Build" stage
                    - Medium app (10–30 files): 2–3 stages
                    - Large app / complex integration: 3–5 stages

                    After calling with planType="implementation", write ONE message rendering the full plan as markdown and asking whether to proceed. Do not call any other tools.
                    """,
                InputSchema = JsonDocument.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "planType": {
                                "type": "string",
                                "enum": ["implementation", "stage_complete"],
                                "description": "Type: implementation (full plan with components + stages) or stage_complete (mark stage done)"
                            },
                            "title": { "type": "string", "description": "Short title for the plan" },
                            "summary": { "type": "string", "description": "1-3 sentence overview" },
                            "components": {
                                "type": "array",
                                "description": "List of components/modules to be built",
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
                                "description": "Key technology/approach decisions. Be specific and actionable — the implementation model will follow these exactly. Include: exact libraries and methods to use, Python 3.13+ compatibility notes, binary dependencies, how data is passed (files must be base64 strings), and any known gotchas. Bad: 'use pydub for audio'. Good: 'use subprocess to call ffmpeg directly — pydub is incompatible with Python 3.13+'.",
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
                                "description": "Ordered list of build stages",
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
            ProposePlanName => await ProposePlan(arguments, context),
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
            var isRefinement = false;

            switch (planType)
            {
                case "implementation":
                    planFilePath = ".plans/IMPLEMENTATION.md";
                    var isInPlanMode = context?.TryGetValue("mode", out var modeVal) == true
                                      && string.Equals(modeVal?.ToString(), "plan", StringComparison.OrdinalIgnoreCase);
                    var existingPlan = await fs.ReadFileAsync(planFilePath);
                    isRefinement = existingPlan != null;
                    // In plan mode, allow overwriting — the user is refining the plan.
                    // In agent mode, silently acknowledge and redirect to implementation.
                    if (!isInPlanMode && isRefinement)
                    {
                        return new McpToolResult
                        {
                            Success = true,
                            Content = "{\"instruction\":\"Plan already exists. Do NOT call propose_plan again. Proceed with implementation using write_file/edit_file, or if implementation is already complete, STOP and write a summary.\",\"planType\":\"implementation\",\"alreadyExists\":true}"
                        };
                    }
                    markdownContent = BuildImplementationMarkdown(title, summary, arguments);
                    break;

                case "stage_complete":
                    planFilePath = string.Empty; // content handled inside
                    var (updatedMarkdown, stageError) = await UpdateStageCompleteAsync(fs, arguments);
                    if (stageError != null)
                        return new McpToolResult { Success = false, Error = stageError };
                    markdownContent = updatedMarkdown;
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

            var nextStageInfo = planType == "stage_complete"
                ? FindNextStage(markdownContent)
                : null;

            // Plain-English instruction included in the result so the model knows exactly what to do next.
            string instruction;
            if (planType == "stage_complete")
            {
                if (!nextStageInfo.HasValue)
                {
                    instruction = "All stages complete. STOP all tool calls. Write ONE final summary for the user listing what was built.";
                }
                else if (nextStageInfo.Value.Complexity == "high")
                {
                    // High-complexity stages: pause so the user can review before the AI continues.
                    instruction = $"Stage complete. Briefly summarise what was built, then ask the user to confirm before you start Stage {nextStageInfo.Value.Index + 1}: {nextStageInfo.Value.Name} — {nextStageInfo.Value.Description}. Do NOT start that stage until they confirm.";
                }
                else
                {
                    // Low/medium stages: proceed automatically — no confirmation needed.
                    instruction = $"Stage complete. Do NOT write any message. Do NOT ask for confirmation. Immediately start implementing Stage {nextStageInfo.Value.Index + 1}: {nextStageInfo.Value.Name} — {nextStageInfo.Value.Description}. Call stage_complete when done.";
                }
            }
            else
            {
                instruction = isRefinement
                    ? "Plan updated. Re-render the full updated plan as markdown (title, summary, components, tech decisions, stages with complexity badges) then ask whether to proceed. Do NOT call any more tools."
                    : "Plan saved. Render the full plan as markdown in your reply: start with the title and 1-sentence summary, then a **Components** section, a **Technical Decisions** section, and a **Stages** section listing each stage with its complexity (e.g. `_(complexity: medium)_`) and description. End with a short question asking whether to proceed. Do NOT call any other tools until confirmed.";
            }

            var pauseForHighStage = planType == "stage_complete" &&
                                    nextStageInfo.HasValue &&
                                    nextStageInfo.Value.Complexity == "high";

            var result = new
            {
                instruction,
                awaitingApproval = planType != "stage_complete" || pauseForHighStage,
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
                hasMoreStages = nextStageInfo.HasValue,
                nextStage = nextStageInfo.HasValue
                    ? new { index = nextStageInfo.Value.Index, name = nextStageInfo.Value.Name, description = nextStageInfo.Value.Description, complexity = nextStageInfo.Value.Complexity }
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

    private static string BuildImplementationMarkdown(string title, string summary, JsonElement args)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Plan: {title}");
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

    private static async Task<(string Markdown, string? Error)> UpdateStageCompleteAsync(IScopedFileSystem fs, JsonElement args)
    {
        var stageIndex = args.TryGetProperty("stageIndex", out var si) ? si.GetInt32() : -1;
        var existing   = await fs.ReadFileAsync(".plans/IMPLEMENTATION.md");

        if (existing is null || stageIndex < 0)
            return (existing ?? string.Empty, null);

        // Detect already-completed stage — return an error so the model doesn't repeat the call.
        var alreadyDoneMarker = $"- [x] Stage {stageIndex + 1} ·";
        if (existing.Contains(alreadyDoneMarker))
            return (existing, $"Stage {stageIndex + 1} was already marked complete. Do not call stage_complete for this stage again.");

        // Replace "- [ ] Stage {stageIndex+1}" with "- [x]" and append completion note
        var marker  = $"- [ ] Stage {stageIndex + 1} ·";
        var replace = $"- [x] Stage {stageIndex + 1} · ";

        if (!existing.Contains(marker))
            return (existing, $"Stage {stageIndex + 1} does not exist in the plan. Check .plans/IMPLEMENTATION.md with read_file and use the correct stageIndex.");

        var updated = existing.Replace(marker, replace, StringComparison.Ordinal);

        // Append a completion log at the bottom
        var summary = args.TryGetProperty("stageSummary", out var ss) ? ss.GetString() : null;
        if (!string.IsNullOrEmpty(summary))
        {
            updated += $"\n---\n_Stage {stageIndex + 1} completed on {DateTime.UtcNow:yyyy-MM-dd}: {summary}_\n";
        }

        await fs.WriteFileAsync(".plans/IMPLEMENTATION.md", updated);
        return (updated, null);
    }

    /// <summary>
    /// Parse the first unchecked stage from the updated markdown.
    /// Returns (name, description, complexity, index) or null if all stages are done.
    /// </summary>
    private static (string Name, string Description, string Complexity, int Index)? FindNextStage(string markdown)
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

            // Extract name and complexity: "Name _(complexity: high)_"
            var afterDot = afterStage[(dotPos + 3)..];
            var complexityPos = afterDot.IndexOf(" _(complexity");
            var name = complexityPos >= 0 ? afterDot[..complexityPos].Trim() : afterDot.Trim();

            // Extract complexity value from "_(complexity: high)_"
            var complexity = "medium";
            if (complexityPos >= 0)
            {
                var complexityPart = afterDot[complexityPos..];
                var colonPos = complexityPart.IndexOf(": ");
                var closePos = complexityPart.IndexOf(")_");
                if (colonPos >= 0 && closePos > colonPos)
                    complexity = complexityPart[(colonPos + 2)..closePos].Trim().ToLowerInvariant();
            }

            // Look for description on the next line
            var desc = string.Empty;
            if (i + 1 < lines.Length)
            {
                var nextLine = lines[i + 1].TrimStart();
                if (nextLine.StartsWith("> "))
                    desc = nextLine[2..].Trim();
            }

            return (name, desc, complexity, zeroBasedIndex);
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
