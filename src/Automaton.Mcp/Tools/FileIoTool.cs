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
    public const string DeleteFolderName = "delete_folder";
    public const string ListFilesName  = "list_files";
    public const string SearchFileName = "search_file";

    private static readonly HashSet<string> HandledTools = [ReadFileName, WriteFileName, EditFileName, DeleteFileName, RenameFileName, RenameFolderName, DeleteFolderName, ListFilesName, SearchFileName];

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
                Description = "Write a file with concrete content. Overwrites the file if it already exists — never delete or empty a file before writing it. Creates directories as needed.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}""")
            },
            new McpTool
            {
                Name = EditFileName,
                Capability = ToolCapability.Write,
                Description = "Make targeted edits to a file by replacing specific text fragments. Preferred over write_file for modifications — only changes what needs changing. Each edit replaces oldText with newText.",
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
                            }
                        },
                        "required": ["path", "edits"]
                    }
                    """)
            },
            new McpTool
            {
                Name = DeleteFileName,
                Capability = ToolCapability.Write,
                Description = "Delete one or more files permanently. Only use when the user explicitly asks to remove files — never call this to replace file content (use write_file instead, which overwrites).",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"paths":{"type":"array","items":{"type":"string"},"description":"One or more file paths to delete."}},"required":["paths"]}""")
            },
            new McpTool
            {
                Name = DeleteFolderName,
                Capability = ToolCapability.Write,
                Description = "Delete a folder and all its contents. Only use when the user explicitly asks to remove a folder.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""")
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
            DeleteFileName   => await DeleteFile(fs, arguments),
            DeleteFolderName => await DeleteFolder(fs, arguments),
            RenameFileName   => await RenameFile(fs, arguments),
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
                content = $"[NOTE: {lineCount} lines — for targeted edits prefer search_file + edit_file]\n\n" + content;

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

            if (string.IsNullOrWhiteSpace(content))
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Refusing to write empty content to '{path}'. You must provide the full file content."
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

                appliedCount++;
            }

            await fs.WriteFileAsync(path, updated);

            return new McpToolResult { Success = true, Content = $"Applied {appliedCount} edit(s) to '{path}'" };
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
            if (!arguments.TryGetProperty("paths", out var pathsProp) || pathsProp.ValueKind != JsonValueKind.Array)
                return new McpToolResult { Success = false, Error = "Missing required argument 'paths' (array of file paths)." };

            var deleted = new List<string>();
            foreach (var el in pathsProp.EnumerateArray())
            {
                var path = el.GetString();
                if (string.IsNullOrWhiteSpace(path)) continue;
                await fs.DeleteFileAsync(path);
                deleted.Add(path);
            }

            return new McpToolResult { Success = true, Content = $"Deleted {deleted.Count} file(s): {string.Join(", ", deleted)}" };
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

    private async Task<McpToolResult> DeleteFolder(IScopedFileSystem fs, JsonElement arguments)
    {
        try
        {
            if (!arguments.TryGetProperty("path", out var pathProp) || pathProp.ValueKind != JsonValueKind.String)
                return new McpToolResult { Success = false, Error = "Missing required argument 'path'." };

            var path = pathProp.GetString()!;
            await fs.DeleteFolderAsync(path);
            return new McpToolResult { Success = true, Content = $"Folder '{path}' deleted successfully" };
        }
        catch (InvalidOperationException ex)
        {
            return new McpToolResult { Success = false, Error = ex.Message };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting folder");
            return new McpToolResult { Success = false, Error = $"Failed to delete folder: {ex.Message}" };
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
