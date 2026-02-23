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
                Name = "get_current_code",
                Description = "Get index.html content",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""")
            },
            new McpTool
            {
                Name = "save_app_code",
                Description = "Save HTML to index.html",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"code":{"type":"string"}},"required":["code"]}""")
            },
            new McpTool
            {
                Name = "read_file",
                Description = "Read a file (e.g. 'styles/main.css', 'js/app.js')",
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
                // Frontend tools
                "get_current_code" => await HandleGetCurrentCode(arguments, context),
                "save_app_code" => await HandleSaveAppCode(arguments, context),
                
                // File-level tools (for multi-file apps)
                "read_file" => await HandleReadFile(arguments, context),
                "write_file" => await HandleWriteFile(arguments, context),
                "delete_file" => await HandleDeleteFile(arguments, context),
                "list_files" => await HandleListFiles(arguments, context),

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

    #region Frontend Tool Handlers

    private async Task<McpToolResult> HandleGetCurrentCode(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();

            // Read index.html (main app file)
            var content = await _appFileStorageService.ReadAppFileAsync(userId, appId, "index.html");

            if (content == null)
            {
                return new McpToolResult
                {
                    Success = true,
                    Content = "<!-- No code found. Start with a new HTML template -->\n<!DOCTYPE html>\n<html>\n<head>\n    <title>New App</title>\n    <script src=\"https://cdn.tailwindcss.com\"></script>\n</head>\n<body>\n    <!-- Add your content here -->\n</body>\n</html>"
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
            _logger.LogError(ex, "Error getting current code");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to get current code: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> HandleSaveAppCode(JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var code = arguments.GetProperty("code").GetString()!;
            var appId = ExtractAppId(arguments, context);
            var userId = _userContextService.GetUserId();

            // Save to index.html
            await _appFileStorageService.WriteAppFileAsync(userId, appId, "index.html", code);

            return new McpToolResult
            {
                Success = true,
                Content = $"Saved index.html ({code.Split('\n').Length} lines)"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving app code");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to save app code: {ex.Message}"
            };
        }
    }

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
