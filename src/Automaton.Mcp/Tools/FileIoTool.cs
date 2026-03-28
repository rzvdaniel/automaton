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
    public const string ReadFileName   = "read_file";
    public const string WriteFileName  = "write_file";
    public const string EditFileName   = "edit_file";
    public const string DeleteFileName = "delete_file";
    public const string RenameFileName = "rename_file";
    public const string RenameFolderName = "rename_folder";
    public const string ListFilesName  = "list_files";
    public const string SearchFileName = "search_file";

    private static readonly HashSet<string> HandledTools = [ReadFileName, WriteFileName, EditFileName, DeleteFileName, RenameFileName, RenameFolderName, ListFilesName, SearchFileName];

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
                Name = ReadFileName,
                Capability = ToolCapability.Read,
                Description = "Read a single file by its exact path (e.g. 'index.html' or 'styles/main.css'). One file per call — make parallel calls to read multiple files at once. Use paths exactly as returned by list_files.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string","description":"Exact relative path of the file to read, e.g. 'index.html' or 'js/game.js'"}},"required":["path"]}""")
            },
            new McpTool
            {
                Name = WriteFileName,
                Capability = ToolCapability.Write,
                Description = "Write a file with concrete content. Creates directories as needed. Empty/whitespace content is blocked by default to prevent accidental file wipes; pass allowEmpty=true only when intentionally writing a blank file.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"},"allowEmpty":{"type":"boolean","description":"Set true only when intentionally writing an empty/whitespace file."}},"required":["path","content"]}""")
            },
            new McpTool
            {
                Name = EditFileName,
                Capability = ToolCapability.Write,
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
                Name = DeleteFileName,
                Capability = ToolCapability.Write,
                Description = "Delete a file. IMPORTANT: always call first WITHOUT confirmed=true to get a confirmation prompt. Only pass confirmed=true after the user explicitly approves the deletion.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"},"confirmed":{"type":"boolean","description":"Must be true to actually delete. Omit or set false for a preview."}},"required":["path"]}""")
            },
            new McpTool
            {
                Name = RenameFileName,
                Capability = ToolCapability.Write,
                Description = "Rename or move a file within the current scope.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"fromPath":{"type":"string"},"toPath":{"type":"string"},"overwrite":{"type":"boolean","description":"Set true to replace destination file if it exists."}},"required":["fromPath","toPath"]}""")
            },
            new McpTool
            {
                Name = RenameFolderName,
                Capability = ToolCapability.Write,
                Description = "Rename or move a folder within the current scope.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"fromPath":{"type":"string"},"toPath":{"type":"string"}},"required":["fromPath","toPath"]}""")
            },
            new McpTool
            {
                Name = ListFilesName,
                Capability = ToolCapability.Read,
                Description = "List all files in the current scope.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""")
            },
            new McpTool
            {
                Name = SearchFileName,
                Capability = ToolCapability.Read,
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
            ReadFileName   => await ReadFile(fs, arguments),
            WriteFileName  => await WriteFile(fs, arguments),
            EditFileName   => await EditFile(fs, arguments),
            DeleteFileName => await DeleteFile(fs, arguments),
            RenameFileName => await RenameFile(fs, arguments),
            RenameFolderName => await RenameFolder(fs, arguments),
            ListFilesName  => await ListFiles(fs),
            SearchFileName => await SearchFile(fs, arguments),
            _ => new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" }
        };
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private async Task<McpToolResult> ReadFile(IScopedFileSystem fs, JsonElement arguments)
    {
        try
        {
            if (!arguments.TryGetProperty("path", out var pathProp) || pathProp.ValueKind != JsonValueKind.String)
                return new McpToolResult { Success = false, Error = "Missing required argument 'path'. Call read_file with {\"path\": \"<exact-file-path>\"}. Use list_files to see available paths." };

            var path    = pathProp.GetString()!;
            var content = await fs.ReadFileAsync(path);

            if (content is null)
                return new McpToolResult
                {
                    Success = false,
                    Error = $"File '{path}' not found. Do NOT retry this path. Only read files that appear in the list_files result. Call list_files first if you are unsure what files exist."
                };

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
            if (!arguments.TryGetProperty("path", out var pathProp) || pathProp.ValueKind != JsonValueKind.String)
                return new McpToolResult { Success = false, Error = "Missing required argument 'path'." };
            if (!arguments.TryGetProperty("content", out var contentProp) || contentProp.ValueKind != JsonValueKind.String)
                return new McpToolResult { Success = false, Error = "Missing required argument 'content'." };

            var path    = pathProp.GetString()!;
            var content = contentProp.GetString()!;
            var allowEmpty = arguments.TryGetProperty("allowEmpty", out var allowEmptyProp) &&
                             allowEmptyProp.ValueKind == JsonValueKind.True;

            if (string.IsNullOrWhiteSpace(content) && !allowEmpty)
            {
                var existingContent = await fs.ReadFileAsync(path);
                if (!string.IsNullOrWhiteSpace(existingContent))
                {
                    return new McpToolResult
                    {
                        Success = false,
                        Error = $"Refusing to overwrite non-empty file '{path}' with empty content. Provide full content, use edit_file for targeted edits, or call write_file with allowEmpty=true if this is intentional."
                    };
                }

                return new McpToolResult
                {
                    Success = false,
                    Error = "Refusing to write empty/whitespace content. Provide actual content or set allowEmpty=true if you intentionally want a blank file."
                };
            }

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
            if (!arguments.TryGetProperty("path", out var pathProp) || pathProp.ValueKind != JsonValueKind.String)
                return new McpToolResult { Success = false, Error = "Missing required argument 'path'." };

            var path   = pathProp.GetString()!;
            var dryRun = arguments.TryGetProperty("dryRun", out var dryRunProp) && dryRunProp.GetBoolean();

            var content = await fs.ReadFileAsync(path);
            if (content is null)
                return new McpToolResult
                {
                    Success = false,
                    Error = $"File '{path}' not found. Do NOT retry. Only edit files that appear in the list_files result."
                };

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
            if (!arguments.TryGetProperty("path", out var pathProp) || pathProp.ValueKind != JsonValueKind.String)
                return new McpToolResult { Success = false, Error = "Missing required argument 'path'." };

            var path      = pathProp.GetString()!;
            var confirmed = arguments.TryGetProperty("confirmed", out var c) && c.GetBoolean();

            if (!confirmed)
            {
                var preview = JsonSerializer.Serialize(new
                {
                    awaitingApproval = true,
                    action           = "delete_file",
                    path,
                    message          = $"About to permanently delete '{path}'. This cannot be undone. Call delete_file again with confirmed=true to proceed, or do nothing to cancel."
                });
                return new McpToolResult { Success = true, Content = preview };
            }

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

    private async Task<McpToolResult> RenameFile(IScopedFileSystem fs, JsonElement arguments)
    {
        try
        {
            if (!arguments.TryGetProperty("fromPath", out var fromPathProp) || fromPathProp.ValueKind != JsonValueKind.String)
                return new McpToolResult { Success = false, Error = "Missing required argument 'fromPath'." };
            if (!arguments.TryGetProperty("toPath", out var toPathProp) || toPathProp.ValueKind != JsonValueKind.String)
                return new McpToolResult { Success = false, Error = "Missing required argument 'toPath'." };

            var fromPath = fromPathProp.GetString()!;
            var toPath = toPathProp.GetString()!;
            var overwrite = arguments.TryGetProperty("overwrite", out var overwriteProp) && overwriteProp.GetBoolean();

            await fs.RenameFileAsync(fromPath, toPath, overwrite);

            return new McpToolResult { Success = true, Content = $"Renamed file '{fromPath}' to '{toPath}'" };
        }
        catch (InvalidOperationException ex)
        {
            return new McpToolResult { Success = false, Error = ex.Message };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renaming file");
            return new McpToolResult { Success = false, Error = $"Failed to rename file: {ex.Message}" };
        }
    }

    private async Task<McpToolResult> RenameFolder(IScopedFileSystem fs, JsonElement arguments)
    {
        try
        {
            if (!arguments.TryGetProperty("fromPath", out var fromPathProp) || fromPathProp.ValueKind != JsonValueKind.String)
                return new McpToolResult { Success = false, Error = "Missing required argument 'fromPath'." };
            if (!arguments.TryGetProperty("toPath", out var toPathProp) || toPathProp.ValueKind != JsonValueKind.String)
                return new McpToolResult { Success = false, Error = "Missing required argument 'toPath'." };

            var fromPath = fromPathProp.GetString()!;
            var toPath = toPathProp.GetString()!;

            await fs.RenameFolderAsync(fromPath, toPath);

            return new McpToolResult { Success = true, Content = $"Renamed folder '{fromPath}' to '{toPath}'" };
        }
        catch (InvalidOperationException ex)
        {
            return new McpToolResult { Success = false, Error = ex.Message };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renaming folder");
            return new McpToolResult { Success = false, Error = $"Failed to rename folder: {ex.Message}" };
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
            if (!arguments.TryGetProperty("path", out var pathProp) || pathProp.ValueKind != JsonValueKind.String)
                return new McpToolResult { Success = false, Error = "Missing required argument 'path'." };
            if (!arguments.TryGetProperty("pattern", out var patternProp) || patternProp.ValueKind != JsonValueKind.String)
                return new McpToolResult { Success = false, Error = "Missing required argument 'pattern'." };

            var path         = pathProp.GetString()!;
            var pattern      = patternProp.GetString()!;
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
