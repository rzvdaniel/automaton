using System.Text.Json;
using Microsoft.Extensions.Logging;
using Automaton.Mcp.Services;
using Automaton.Mcp.Models;
using Automaton.Studio.Server.Models;

namespace Automaton.Studio.Server.Services.Mcp;

/// <summary>
/// Server-side implementation of MCP tools for Web App editing
/// </summary>
public class AppMcpToolProvider : IMcpToolProvider
{
    private readonly ILogger<AppMcpToolProvider> _logger;
    private readonly AppsService _appsService;
    private readonly AppFileStorageService _appFileStorageService;
    private readonly AppStepsService _appStepsService;
    private readonly CustomStepsService _customStepsService;
    private readonly UserContextService _userContextService;

    public AppMcpToolProvider(
        ILogger<AppMcpToolProvider> logger,
        AppsService appsService,
        AppFileStorageService appFileStorageService,
        AppStepsService appStepsService,
        CustomStepsService customStepsService,
        UserContextService userContextService)
    {
        _logger = logger;
        _appsService = appsService;
        _appFileStorageService = appFileStorageService;
        _appStepsService = appStepsService;
        _customStepsService = customStepsService;
        _userContextService = userContextService;
    }

    public Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        var tools = new List<McpTool>
        {
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
                            "description": "The ID of the app"
                        }
                    },
                    "required": ["appId"]
                }
                """)
            },
            new McpTool
            {
                Name = "read_file",
                Description = "Read a specific file from the app",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app"
                        },
                        "filePath": {
                            "type": "string",
                            "description": "Path to the file (e.g., 'index.html', 'css/styles.css')"
                        }
                    },
                    "required": ["appId", "filePath"]
                }
                """)
            },
            new McpTool
            {
                Name = "write_file",
                Description = "Write or update a file in the app",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app"
                        },
                        "filePath": {
                            "type": "string",
                            "description": "Path to the file"
                        },
                        "content": {
                            "type": "string",
                            "description": "File content"
                        }
                    },
                    "required": ["appId", "filePath", "content"]
                }
                """)
            },
            new McpTool
            {
                Name = "delete_file",
                Description = "Delete a file from the app",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app"
                        },
                        "filePath": {
                            "type": "string",
                            "description": "Path to the file"
                        }
                    },
                    "required": ["appId", "filePath"]
                }
                """)
            },
            new McpTool
            {
                Name = "create_backend_step",
                Description = "Create a new backend custom step and link it to the app",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app"
                        },
                        "stepName": {
                            "type": "string",
                            "description": "Name of the step"
                        },
                        "language": {
                            "type": "string",
                            "enum": ["Python", "PowerShell", "JavaScript"],
                            "description": "Programming language"
                        },
                        "code": {
                            "type": "string",
                            "description": "Initial code for the step"
                        },
                        "description": {
                            "type": "string",
                            "description": "Description of the step"
                        }
                    },
                    "required": ["appId", "stepName", "language"]
                }
                """)
            },
            new McpTool
            {
                Name = "list_backend_steps",
                Description = "List all backend steps linked to the app",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "appId": {
                            "type": "string",
                            "description": "The ID of the app"
                        }
                    },
                    "required": ["appId"]
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
                "get_current_code" => await GetCurrentCodeAsync(arguments, contextId),
                "read_file" => await ReadFileAsync(arguments, contextId),
                "write_file" => await WriteFileAsync(arguments, contextId),
                "delete_file" => await DeleteFileAsync(arguments, contextId),
                "create_backend_step" => await CreateBackendStepAsync(arguments, contextId),
                "list_backend_steps" => await ListBackendStepsAsync(arguments, contextId),
                _ => new McpToolResult
                {
                    Success = false,
                    Error = $"Unknown tool: {toolName}"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return new McpToolResult
            {
                Success = false,
                Error = $"Error executing {toolName}: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> GetCurrentCodeAsync(JsonElement arguments, string? contextId)
    {
        try
        {
            var appId = Guid.Parse(arguments.GetProperty("appId").GetString()!);
            var userId = _userContextService.GetUserId();

            // Read index.html (main app file)
            var content = await _appFileStorageService.ReadAppFileAsync(userId, appId, "index.html");

            if (content == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "App file not found"
                };
            }

            var result = new
            {
                appId = appId.ToString(),
                code = content,
                message = "Current app code retrieved successfully"
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

    private async Task<McpToolResult> ReadFileAsync(JsonElement arguments, string? contextId)
    {
        try
        {
            var appId = Guid.Parse(arguments.GetProperty("appId").GetString()!);
            var filePath = arguments.GetProperty("filePath").GetString()!;
            var userId = _userContextService.GetUserId();

            var content = await _appFileStorageService.ReadAppFileAsync(userId, appId, filePath);

            if (content == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"File '{filePath}' not found"
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

    private async Task<McpToolResult> WriteFileAsync(JsonElement arguments, string? contextId)
    {
        try
        {
            var appId = Guid.Parse(arguments.GetProperty("appId").GetString()!);
            var filePath = arguments.GetProperty("filePath").GetString()!;
            var content = arguments.GetProperty("content").GetString()!;
            var userId = _userContextService.GetUserId();

            await _appFileStorageService.WriteAppFileAsync(userId, appId, filePath, content);

            return new McpToolResult
            {
                Success = true,
                Content = $"File '{filePath}' written successfully"
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

    private async Task<McpToolResult> DeleteFileAsync(JsonElement arguments, string? contextId)
    {
        try
        {
            var appId = Guid.Parse(arguments.GetProperty("appId").GetString()!);
            var filePath = arguments.GetProperty("filePath").GetString()!;
            var userId = _userContextService.GetUserId();

            await _appFileStorageService.DeleteAppFileAsync(userId, appId, filePath);

            return new McpToolResult
            {
                Success = true,
                Content = $"File '{filePath}' deleted successfully"
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

    private async Task<McpToolResult> CreateBackendStepAsync(JsonElement arguments, string? contextId)
    {
        try
        {
            var appId = Guid.Parse(arguments.GetProperty("appId").GetString()!);
            var stepName = arguments.GetProperty("stepName").GetString()!;
            var language = arguments.GetProperty("language").GetString()!;
            var code = arguments.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : null;
            var description = arguments.TryGetProperty("description", out var descProp) ? descProp.GetString() : string.Empty;

            // Create and link the step
            var linkedStep = await _appStepsService.CreateStepForAppAsync(appId, stepName);

            // Update the step with code and other properties if provided
            if (!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(description) || !string.IsNullOrEmpty(language))
            {
                var step = _customStepsService.Get(linkedStep.Id);
                if (step != null)
                {
                    if (!string.IsNullOrEmpty(code))
                        step.Definition.Code = code;
                    if (!string.IsNullOrEmpty(description))
                        step.Description = description;
                    if (!string.IsNullOrEmpty(language))
                        step.Definition.Language = language;

                    await _customStepsService.Update(linkedStep.Id, step);
                }
            }

            var result = new
            {
                stepId = linkedStep.Id,
                stepName = linkedStep.Name,
                message = $"Backend step '{stepName}' created and linked successfully"
            };

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating backend step");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to create backend step: {ex.Message}"
            };
        }
    }

    private async Task<McpToolResult> ListBackendStepsAsync(JsonElement arguments, string? contextId)
    {
        try
        {
            var appId = Guid.Parse(arguments.GetProperty("appId").GetString()!);

            var steps = await _appStepsService.GetLinkedStepsAsync(appId);

            var result = steps.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                language = s.Language,
                description = s.Description
            });

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing backend steps");
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to list backend steps: {ex.Message}"
            };
        }
    }
}
