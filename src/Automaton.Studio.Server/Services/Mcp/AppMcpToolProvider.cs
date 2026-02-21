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
            // Frontend tools
            new McpTool
            {
                Name = "get_current_code",
                Description = "Get the current HTML/CSS/JavaScript code from the app",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    }
                }
                """)
            },
            new McpTool
            {
                Name = "save_app_code",
                Description = "Save HTML/CSS/JavaScript code to the app",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "code": {
                            "type": "string",
                            "description": "The HTML/CSS/JavaScript code to save"
                        },
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    },
                    "required": ["code"]
                }
                """)
            },

            // File-level tools (for multi-file apps)
            new McpTool
            {
                Name = "read_file",
                Description = "Read a specific file from the app (for multi-file apps with separate CSS/JS files)",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "path": {
                            "type": "string",
                            "description": "Relative path to the file (e.g., 'styles/main.css', 'js/app.js', 'index.html')"
                        },
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    },
                    "required": ["path"]
                }
                """)
            },
            new McpTool
            {
                Name = "write_file",
                Description = "Write a specific file to the app (for multi-file apps). Creates directories as needed. Use for CSS, JS, or component files.",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "path": {
                            "type": "string",
                            "description": "Relative path to the file (e.g., 'styles/main.css', 'js/app.js')"
                        },
                        "content": {
                            "type": "string",
                            "description": "File content to write"
                        },
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    },
                    "required": ["path", "content"]
                }
                """)
            },
            new McpTool
            {
                Name = "delete_file",
                Description = "Delete a specific file from the app (for multi-file apps). Cannot delete index.html.",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "path": {
                            "type": "string",
                            "description": "Relative path to the file to delete (e.g., 'styles/old.css')"
                        },
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    },
                    "required": ["path"]
                }
                """)
            },
            new McpTool
            {
                Name = "list_files",
                Description = "List all files in the app directory (useful for multi-file apps)",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app (optional, uses context if not provided)"
                        }
                    }
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

            var result = new
            {
                code = content,
                linesOfCode = content.Split('\n').Length,
                message = "Current HTML code retrieved successfully"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
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

            var result = new
            {
                success = true,
                appId = appId.ToString(),
                linesOfCode = code.Split('\n').Length,
                message = "App code saved successfully"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
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

            var result = new
            {
                path = path,
                content = content,
                size = content.Length,
                lines = content.Split('\n').Length
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
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

            var result = new
            {
                path = path,
                size = content.Length,
                lines = content.Split('\n').Length,
                message = $"File '{path}' saved successfully"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
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

            var result = new
            {
                files = files.ToList(),
                count = files.Count(),
                message = $"Found {files.Count()} file(s)"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
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
