using System.Text.Json;
using Automaton.Mcp.Services;
using Automaton.Mcp.Models;

namespace Automaton.Studio.Server.Services.Mcp;

/// <summary>
/// Server-side implementation of MCP tools for Web App editing with backend controller support
/// </summary>
public class AppMcpToolProvider : IMcpToolProvider
{
    private readonly ILogger<AppMcpToolProvider> _logger;
    private readonly AppsService _appsService;
    private readonly AppFileStorageService _appFileStorageService;
    private readonly UserContextService _userContextService;

    public AppMcpToolProvider(
        ILogger<AppMcpToolProvider> logger,
        AppsService appsService,
        AppFileStorageService appFileStorageService,
        UserContextService userContextService)
    {
        _logger = logger;
        _appsService = appsService;
        _appFileStorageService = appFileStorageService;
        _userContextService = userContextService;
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
                Description = "Write a file. Creates dirs as needed.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}""")
            },
            new McpTool
            {
                Name = "delete_file",
                Description = "Delete a file. Cannot delete index.html.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""")
            },
            new McpTool
            {
                Name = "list_files",
                Description = "List all app files",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""")
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
            }
        };

        return Task.FromResult<IEnumerable<McpTool>>(tools);
    }

    public async Task<McpToolResult> ExecuteToolAsync(
        string toolName,
        JsonElement arguments,
        string? contextId = null,
        Dictionary<string, object>? context = null)
    {
        try
        {
            return toolName switch
            {
                "read_file" => await HandleReadFile(arguments, context),
                "write_file" => await HandleWriteFile(arguments, context),
                "delete_file" => await HandleDeleteFile(arguments, context),
                "list_files" => await HandleListFiles(arguments, context),
                "edit_file" => await HandleEditFile(arguments, context),

                _ => new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return new McpToolResult
            {
                Success = false,
                Error = $"Tool execution failed: {ex.Message}"
            };
        }
    }

    #region Tool Handlers

    private async Task<McpToolResult> HandleReadFile(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var path = arguments.GetProperty("path").GetString()!;
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();

            var content = await _appFileStorageService.ReadAppFileAsync(userId, appId, path);

            if (content == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"File '{path}' not found"
                };
            }

            return new McpToolResult
            {
                Success = true,
                Content = content
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to read file: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleWriteFile(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var path = arguments.GetProperty("path").GetString()!;
            var content = arguments.GetProperty("content").GetString()!;
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();

            await _appFileStorageService.WriteAppFileAsync(userId, appId, path, content);

            return new McpToolResult
            {
                Success = true,
                Content = $"Saved {path} ({content.Split('\n').Length} lines)"
            };
        }
        catch (InvalidOperationException ex)
        {
            // File extension validation error
            return new McpToolResult
            {
                Success = false,
                Error = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing file");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to write file: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleDeleteFile(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var path = arguments.GetProperty("path").GetString()!;
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();

            // Prevent deletion of index.html
            if (path.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "Cannot delete index.html - it's the main app file. Use save_app_code to modify it instead."
                };
            }

            await _appFileStorageService.DeleteAppFileAsync(userId, appId, path);

            return new McpToolResult
            {
                Success = true,
                Content = $"File '{path}' deleted successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to delete file: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleListFiles(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();

            var files = await _appFileStorageService.GetAppFilesAsync(userId, appId);

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(files)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to list files: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleEditFile(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var path = arguments.GetProperty("path").GetString()!;
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();
            var dryRun = arguments.TryGetProperty("dryRun", out var dryRunProp) && dryRunProp.GetBoolean();

            var content = await _appFileStorageService.ReadAppFileAsync(userId, appId, path);
            if (content == null)
            {
                return new McpToolResult { Success = false, Error = $"File '{path}' not found" };
            }

            if (!arguments.TryGetProperty("edits", out var editsElement) || editsElement.ValueKind != JsonValueKind.Array)
            {
                return new McpToolResult { Success = false, Error = "Missing or invalid 'edits' array" };
            }

            var updatedContent = content;
            var appliedCount = 0;
            var summary = new System.Text.StringBuilder();

            foreach (var edit in editsElement.EnumerateArray())
            {
                var oldText = edit.GetProperty("oldText").GetString()!;
                var newText = edit.GetProperty("newText").GetString()!;

                if (!updatedContent.Contains(oldText))
                {
                    return new McpToolResult
                    {
                        Success = false,
                        Error = $"Edit {appliedCount + 1}: oldText not found in file. Make sure oldText matches exactly (including whitespace)."
                    };
                }

                // Replace only the first occurrence to avoid unintended changes
                var idx = updatedContent.IndexOf(oldText, StringComparison.Ordinal);
                updatedContent = updatedContent[..idx] + newText + updatedContent[(idx + oldText.Length)..];

                var oldLines = oldText.Split('\n').Length;
                var newLines = newText.Split('\n').Length;
                summary.AppendLine($"- Edit {appliedCount + 1}: replaced {oldLines} line(s) with {newLines} line(s)");
                appliedCount++;
            }

            if (dryRun)
            {
                return new McpToolResult
                {
                    Success = true,
                    Content = $"[Dry run] {appliedCount} edit(s) would be applied to '{path}':\n{summary}"
                };
            }

            await _appFileStorageService.WriteAppFileAsync(userId, appId, path, updatedContent);

            return new McpToolResult
            {
                Success = true,
                Content = $"Applied {appliedCount} edit(s) to '{path}':\n{summary}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error editing file");
            return new McpToolResult { Success = false, Error = $"Failed to edit file: {ex.Message}" };
        }
    }

    #endregion

    #region Helper Methods

    private Guid ExtractAppId(JsonElement arguments, Dictionary<string, object>? context)
    {
        // Try to get appId from arguments first
        if (arguments.TryGetProperty("appId", out var appIdProp))
        {
            var appIdStr = appIdProp.GetString();
            if (!string.IsNullOrEmpty(appIdStr) && Guid.TryParse(appIdStr, out var appId))
            {
                return appId;
            }
        }

        // Fall back to context (resourceId)
        if (context != null && context.TryGetValue("resourceId", out var resourceIdObj))
        {
            if (resourceIdObj is Guid resourceGuid)
            {
                return resourceGuid;
            }
            if (resourceIdObj is string resourceStr && Guid.TryParse(resourceStr, out var resourceId))
            {
                return resourceId;
            }
        }

        throw new InvalidOperationException("No valid appId provided in arguments or context");
    }

    #endregion
}
