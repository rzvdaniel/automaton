using System.Text;
using System.Text.Json;
using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Mcp.Tools;

/// <summary>
/// Generic file IO MCP tool group: read, write, edit, delete, list.
/// Has zero knowledge of apps, flows, or any specific resource type —
/// all file access is delegated to <see cref="IScopedFileSystemFactory"/>.
/// </summary>
public class FileIoTool : IMcpTool
{
    private static readonly HashSet<string> HandledTools = ["read_file", "write_file", "edit_file", "delete_file", "list_files", "search_file"];

    private readonly IScopedFileSystemFactory _fileSystemFactory;
    private readonly ILogger<FileIoTool> _logger;

    public FileIoTool(IScopedFileSystemFactory fileSystemFactory, ILogger<FileIoTool> logger)
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
                Name = "read_file",
                Description = "Read a file (e.g. 'index.html', 'styles/main.css', 'js/app.js')",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""")
            },
            new McpTool
            {
                Name = "write_file",
                Description = "Write a file. Creates directories as needed.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}""")
            },
            new McpTool
            {
                Name = "edit_file",
                Description = "Make targeted edits to a file by replacing specific text fragments. Preferred over write_file for modifications — only changes what needs changing. Each edit replaces oldText with newText. Use dryRun=true to preview without saving.",
                InputSchema = JsonDocument.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "path": { "type": "string" },
                            "edits": {
                                "type": "array",
                                "items": {
                                    "type": "object",
                                    "properties": {
                                        "oldText": { "type": "string" },
                                        "newText": { "type": "string" }
                                    },
                                    "required": ["oldText", "newText"]
                                }
                            },
                            "dryRun": { "type": "boolean" }
                        },
                        "required": ["path", "edits"]
                    }
                    """)
            },
            new McpTool
            {
                Name = "delete_file",
                Description = "Delete a file.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""")
            },
            new McpTool
            {
                Name = "list_files",
                Description = "List all files in the current scope.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""")
            },
            new McpTool
            {
                Name = "search_file",
                Description = "Search for lines matching a pattern inside a file. Returns each match with surrounding context lines — use the returned text verbatim as oldText in edit_file for guaranteed accuracy. Prefer this over read_file for large files when you only need to find and change specific sections.",
                InputSchema = JsonDocument.Parse("""
                    {
                        "type": "object",
                        "properties": {
                            "path":         { "type": "string", "description": "File to search" },
                            "pattern":      { "type": "string", "description": "Search pattern (literal string or regex)" },
                            "useRegex":     { "type": "boolean", "description": "Treat pattern as regex (default: false)" },
                            "contextLines": { "type": "integer", "description": "Lines of context to include around each match (default: 3)" }
                        },
                        "required": ["path", "pattern"]
                    }
                    """)
            }
        };

        return Task.FromResult<IEnumerable<McpTool>>(tools);
    }

    public bool CanHandle(string toolName) => HandledTools.Contains(toolName);

    public async Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, Dictionary<string, object>? context)
    {
        var fs = _fileSystemFactory.Create(arguments, context);

        return toolName switch
        {
            "read_file"   => await ReadFile(fs, arguments),
            "write_file"  => await WriteFile(fs, arguments),
            "edit_file"   => await EditFile(fs, arguments),
            "delete_file" => await DeleteFile(fs, arguments),
            "list_files"  => await ListFiles(fs),
            "search_file" => await SearchFile(fs, arguments),
            _ => new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" }
        };
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private async Task<McpToolResult> ReadFile(IScopedFileSystem fs, JsonElement arguments)
    {
        try
        {
            var path    = arguments.GetProperty("path").GetString()!;
            var content = await fs.ReadFileAsync(path);

            if (content is null)
                return new McpToolResult { Success = false, Error = $"File '{path}' not found" };

            var lineCount = content.Count(c => c == '\n') + 1;
            if (lineCount >= LocalScopedFileSystem.LargeFileThreshold)
            {
                // Return hint as a separate JSON field so it is never part of the file content.
                // The AI reads `content` for the file text and `note` as metadata — they stay separate.
                var json = JsonSerializer.Serialize(new
                {
                    note    = $"File has {lineCount} lines. For targeted edits prefer search_file + edit_file to avoid re-reading.",
                    content
                });
                return new McpToolResult { Success = true, Content = json };
            }

            return new McpToolResult { Success = true, Content = content };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file");
            return new McpToolResult { Success = false, Error = $"Failed to read file: {ex.Message}" };
        }
    }

    private async Task<McpToolResult> WriteFile(IScopedFileSystem fs, JsonElement arguments)
    {
        try
        {
            var path    = arguments.GetProperty("path").GetString()!;
            var content = arguments.GetProperty("content").GetString()!;

            await fs.WriteFileAsync(path, content);

            return new McpToolResult { Success = true, Content = $"Saved {path} ({content.Split('\n').Length} lines)" };
        }
        catch (InvalidOperationException ex)
        {
            return new McpToolResult { Success = false, Error = ex.Message };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing file");
            return new McpToolResult { Success = false, Error = $"Failed to write file: {ex.Message}" };
        }
    }

    private async Task<McpToolResult> EditFile(IScopedFileSystem fs, JsonElement arguments)
    {
        try
        {
            var path   = arguments.GetProperty("path").GetString()!;
            var dryRun = arguments.TryGetProperty("dryRun", out var dryRunProp) && dryRunProp.GetBoolean();

            var content = await fs.ReadFileAsync(path);
            if (content is null)
                return new McpToolResult { Success = false, Error = $"File '{path}' not found" };

            if (!arguments.TryGetProperty("edits", out var editsElement) || editsElement.ValueKind != JsonValueKind.Array)
                return new McpToolResult { Success = false, Error = "Missing or invalid 'edits' array" };

            var updated      = content;
            var appliedCount = 0;
            var summary      = new StringBuilder();

            foreach (var edit in editsElement.EnumerateArray())
            {
                var oldText = edit.GetProperty("oldText").GetString()!;
                var newText = edit.GetProperty("newText").GetString()!;

                if (!updated.Contains(oldText))
                    return new McpToolResult
                    {
                        Success = false,
                        Error = $"Edit {appliedCount + 1}: oldText not found in file. Make sure oldText matches exactly (including whitespace)."
                    };

                var idx = updated.IndexOf(oldText, StringComparison.Ordinal);
                updated = updated[..idx] + newText + updated[(idx + oldText.Length)..];

                summary.AppendLine($"- Edit {appliedCount + 1}: replaced {oldText.Split('\n').Length} line(s) with {newText.Split('\n').Length} line(s)");
                appliedCount++;
            }

            if (dryRun)
                return new McpToolResult { Success = true, Content = $"[Dry run] {appliedCount} edit(s) would be applied to '{path}':\n{summary}" };

            await fs.WriteFileAsync(path, updated);

            return new McpToolResult { Success = true, Content = $"Applied {appliedCount} edit(s) to '{path}':\n{summary}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing file");
            return new McpToolResult { Success = false, Error = $"Failed to edit file: {ex.Message}" };
        }
    }

    private async Task<McpToolResult> DeleteFile(IScopedFileSystem fs, JsonElement arguments)
    {
        try
        {
            var path = arguments.GetProperty("path").GetString()!;
            await fs.DeleteFileAsync(path);
            return new McpToolResult { Success = true, Content = $"File '{path}' deleted successfully" };
        }
        catch (InvalidOperationException ex)
        {
            return new McpToolResult { Success = false, Error = ex.Message };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file");
            return new McpToolResult { Success = false, Error = $"Failed to delete file: {ex.Message}" };
        }
    }

    private async Task<McpToolResult> ListFiles(IScopedFileSystem fs)
    {
        try
        {
            var files = await fs.ListFilesAsync();
            return new McpToolResult { Success = true, Content = JsonSerializer.Serialize(files) };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files");
            return new McpToolResult { Success = false, Error = $"Failed to list files: {ex.Message}" };
        }
    }

    private async Task<McpToolResult> SearchFile(IScopedFileSystem fs, JsonElement arguments)
    {
        try
        {
            var path         = arguments.GetProperty("path").GetString()!;
            var pattern      = arguments.GetProperty("pattern").GetString()!;
            var useRegex     = arguments.TryGetProperty("useRegex", out var rx) && rx.GetBoolean();
            var contextLines = arguments.TryGetProperty("contextLines", out var cl) ? cl.GetInt32() : 3;

            var matches = await fs.SearchFileAsync(path, pattern, useRegex, contextLines);
            var list    = matches.ToList();

            if (list.Count == 0)
                return new McpToolResult { Success = true, Content = $"No matches found for '{pattern}' in '{path}'" };

            var result = JsonSerializer.Serialize(list.Select(m => new
            {
                lineNumber = m.LineNumber,
                match      = m.Match,
                context    = m.Context
            }), new JsonSerializerOptions { WriteIndented = true });

            return new McpToolResult { Success = true, Content = $"{list.Count} match(es) in '{path}':\n{result}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching file");
            return new McpToolResult { Success = false, Error = $"Failed to search file: {ex.Message}" };
        }
    }
}
