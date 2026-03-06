using System.Text;
using System.Text.Json;
using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Mcp.Tools;

/// <summary>
/// MCP tool group for AI instruction files: read_instruction, write_instruction.
/// Resource-agnostic — all file access goes through the scoped file system factory.
/// Instruction files live in the .instructions/ subfolder within the resource root.
/// write_instruction also keeps _index.md up to date automatically.
/// </summary>
public class InstructionsTool : IMcpTool
{
    private static readonly HashSet<string> HandledTools = ["read_instruction", "write_instruction", "list_instructions"];

    private readonly IScopedFileSystemFactory _fileSystemFactory;
    private readonly ILogger<InstructionsTool> _logger;

    public InstructionsTool(IScopedFileSystemFactory fileSystemFactory, ILogger<InstructionsTool> logger)
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
                Name = "read_instruction",
                Capability = ToolCapability.Read,
                Description = "Read an instruction file from the .instructions/ folder (e.g. 'frontend.md', 'design.md'). Use this to load relevant preferences before making decisions.",
                InputSchema = JsonDocument.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "filename": { "type": "string", "description": "Filename within .instructions/ e.g. 'frontend.md'" }
                        },
                        "required": ["filename"]
                    }
                    """)
            },
            new McpTool
            {
                Name = "write_instruction",
                Capability = ToolCapability.Write,
                Description = "Write or update an instruction file in the .instructions/ folder. Use this to record design decisions, coding preferences, or conventions so future sessions stay consistent. The _index.md summary is updated automatically.",
                InputSchema = JsonDocument.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "filename": {
                                "type": "string",
                                "description": "Filename within .instructions/ e.g. 'frontend.md'. Use descriptive names: frontend, backend, design, database, testing, etc."
                            },
                            "content": {
                                "type": "string",
                                "description": "Full markdown content. Include YAML frontmatter with priority (high|medium|low) and context (always|on-request)."
                            },
                            "reason": {
                                "type": "string",
                                "description": "Brief reason for creating/updating this file — shown to the user as a notification."
                            }
                        },
                        "required": ["filename", "content"]
                    }
                    """)
            },
            new McpTool
            {
                Name = "list_instructions",
                Capability = ToolCapability.Read,
                Description = "List all instruction files in the .instructions/ folder.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""")
            }
        };

        return Task.FromResult<IEnumerable<McpTool>>(tools);
    }

    public bool CanHandle(string toolName) => HandledTools.Contains(toolName);

    public async Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, Dictionary<string, object>? context)
    {
        return toolName switch
        {
            "read_instruction"  => await ReadInstruction(arguments, context),
            "write_instruction" => await WriteInstruction(arguments, context),
            "list_instructions" => await ListInstructions(arguments, context),
            _ => new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" }
        };
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private async Task<McpToolResult> ReadInstruction(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var filename = arguments.GetProperty("filename").GetString()!;
            var path     = NormalizePath(filename);
            var fs       = _fileSystemFactory.Create(arguments, context);
            var content  = await fs.ReadFileAsync(path);

            return content is null
                ? new McpToolResult { Success = false, Error = $"Instruction file '{filename}' not found in .instructions/" }
                : new McpToolResult { Success = true, Content = content };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading instruction file");
            return new McpToolResult { Success = false, Error = $"Failed to read instruction: {ex.Message}" };
        }
    }

    private async Task<McpToolResult> WriteInstruction(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var filename = arguments.GetProperty("filename").GetString()!;
            var content  = arguments.GetProperty("content").GetString()!;
            var reason   = arguments.TryGetProperty("reason", out var r) ? r.GetString() : null;
            var path     = NormalizePath(filename);
            var fs       = _fileSystemFactory.Create(arguments, context);

            await fs.WriteFileAsync(path, content);
            _logger.LogInformation("Wrote instruction file {Path}", path);

            // Update _index.md
            await UpdateIndexAsync(fs, filename, content, reason);

            // Return a result with a notification marker so the frontend can show a subtle alert
            var result = new
            {
                written       = true,
                filename,
                path,
                reason,
                notification  = $"📝 Updated `{filename}`{(reason != null ? $" — {reason}" : "")}"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing instruction file");
            return new McpToolResult { Success = false, Error = $"Failed to write instruction: {ex.Message}" };
        }
    }

    private async Task<McpToolResult> ListInstructions(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var fs    = _fileSystemFactory.Create(arguments, context);
            var files = await fs.ListFilesAsync();
            var instructions = files
                .Where(f => f.StartsWith(".instructions/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return new McpToolResult
            {
                Success = true,
                Content = instructions.Count == 0
                    ? "No instruction files found."
                    : string.Join("\n", instructions)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing instruction files");
            return new McpToolResult { Success = false, Error = $"Failed to list instructions: {ex.Message}" };
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Ensures path is within .instructions/ regardless of what the AI passes.</summary>
    private static string NormalizePath(string filename)
    {
        // Strip any leading .instructions/ the caller may have included
        var name = filename.TrimStart('/').TrimStart('\\');
        if (name.StartsWith(".instructions/", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(".instructions\\", StringComparison.OrdinalIgnoreCase))
        {
            return name.Replace('\\', '/');
        }
        return $".instructions/{name}";
    }

    private async Task UpdateIndexAsync(IScopedFileSystem fs, string filename, string newContent, string? reason)
    {
        try
        {
            const string indexPath = ".instructions/_index.md";
            var existing = await fs.ReadFileAsync(indexPath) ?? BuildNewIndex();

            // Extract first heading or first non-empty line as a one-line summary
            var summary = ExtractSummary(newContent);

            // Update or add entry for this file
            var entryMarker = $"- `{filename}`";
            var newEntry    = $"- `{filename}` — {summary}";

            string updated;
            if (existing.Contains(entryMarker))
            {
                // Replace existing entry line
                var lines = existing.Split('\n');
                updated = string.Join('\n', lines.Select(l =>
                    l.TrimStart().StartsWith(entryMarker) ? newEntry : l));
            }
            else
            {
                // Append new entry before the footer
                updated = existing.TrimEnd() + $"\n{newEntry}\n";
            }

            if (!string.IsNullOrEmpty(reason))
            {
                updated = updated.TrimEnd() + $"\n\n_Last updated: {DateTime.UtcNow:yyyy-MM-dd} — {reason}_\n";
            }

            await fs.WriteFileAsync(indexPath, updated);
        }
        catch (Exception ex)
        {
            // Non-fatal: index update failure should not prevent the instruction write from succeeding
            _logger.LogWarning(ex, "Failed to update _index.md");
        }
    }

    private static string BuildNewIndex() =>
        """
        # Instructions Index

        This file is auto-maintained. It lists all instruction files and their purpose.
        Files marked `context: always` are injected into every AI session automatically.
        Files marked `context: on-request` are loaded by the AI when relevant.

        ## Files

        """;

    private static string ExtractSummary(string content)
    {
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;
            if (trimmed.StartsWith("---")) continue; // YAML frontmatter delimiter
            if (trimmed.StartsWith("#"))
                return trimmed.TrimStart('#').Trim();
            if (!trimmed.StartsWith('-') && !trimmed.StartsWith(':'))
                return trimmed.Length > 80 ? trimmed[..77] + "…" : trimmed;
        }
        return content[..Math.Min(80, content.Length)];
    }
}
