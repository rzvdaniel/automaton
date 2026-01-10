using System.Text.Json;
using Automaton.Mcp.Models;
using Automaton.Mcp.Services;

namespace Automaton.Studio.Server.Services;

/// <summary>
/// MCP tool provider for App editing context
/// Provides tools for reading/writing app files, managing backend steps
/// </summary>
public class AppMcpToolProvider : IMcpToolProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string? _appId;
    private readonly Dictionary<string, object>? _context;

    public AppMcpToolProvider(
        IServiceProvider serviceProvider,
        string? appId,
        Dictionary<string, object>? context)
    {
        _serviceProvider = serviceProvider;
        _appId = appId;
        _context = context;
    }

    public Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        var tools = new List<McpTool>
        {
            new McpTool
            {
                Name = "get_app_file",
                Description = "Read the content of a specific file in the app",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""filePath"": {
                            ""type"": ""string"",
                            ""description"": ""Path of the file to read (e.g., 'index.html', 'styles.css')""
                        }
                    },
                    ""required"": [""filePath""]
                }")
            },
            new McpTool
            {
                Name = "save_app_file",
                Description = "Save content to a file in the app",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""filePath"": {
                            ""type"": ""string"",
                            ""description"": ""Path of the file to save (e.g., 'index.html', 'app.js')""
                        },
                        ""content"": {
                            ""type"": ""string"",
                            ""description"": ""Content to save to the file""
                        }
                    },
                    ""required"": [""filePath"", ""content""]
                }")
            },
            new McpTool
            {
                Name = "list_app_files",
                Description = "List all files in the app",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {}
                }")
            },
            new McpTool
            {
                Name = "get_linked_steps",
                Description = "Get all backend steps linked to this app",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {}
                }")
            },
            new McpTool
            {
                Name = "create_backend_step",
                Description = "Create a new backend step and link it to this app",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""name"": {
                            ""type"": ""string"",
                            ""description"": ""Name of the step (alphanumeric, no spaces)""
                        },
                        ""displayName"": {
                            ""type"": ""string"",
                            ""description"": ""Display name of the step""
                        },
                        ""description"": {
                            ""type"": ""string"",
                            ""description"": ""Description of what the step does""
                        },
                        ""language"": {
                            ""type"": ""string"",
                            ""enum"": [""Python"", ""PowerShell"", ""Batch""],
                            ""description"": ""Programming language for the step""
                        },
                        ""code"": {
                            ""type"": ""string"",
                            ""description"": ""Code for the step""
                        }
                    },
                    ""required"": [""name"", ""displayName"", ""language"", ""code""]
                }")
            },
            new McpTool
            {
                Name = "execute_backend_step",
                Description = "Execute a backend step to test it",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""stepId"": {
                            ""type"": ""string"",
                            ""description"": ""ID of the step to execute""
                        },
                        ""inputs"": {
                            ""type"": ""object"",
                            ""description"": ""Input variables for the step""
                        }
                    },
                    ""required"": [""stepId""]
                }")
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
            var appId = contextId ?? _appId;
            if (string.IsNullOrEmpty(appId) && toolName != "list_app_files")
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "App ID is required"
                };
            }

            return toolName switch
            {
                "get_app_file" => await GetAppFile(appId!, arguments),
                "save_app_file" => await SaveAppFile(appId!, arguments),
                "list_app_files" => await ListAppFiles(appId!),
                "get_linked_steps" => await GetLinkedSteps(appId!),
                "create_backend_step" => await CreateBackendStep(appId!, arguments),
                "execute_backend_step" => await ExecuteBackendStep(arguments),
                _ => new McpToolResult { Success = false, Error = $"Unknown tool: {toolName}" }
            };
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private async Task<McpToolResult> GetAppFile(string appId, JsonElement arguments)
    {
        var filePath = arguments.GetProperty("filePath").GetString();

        using var scope = _serviceProvider.CreateScope();
        var appsService = scope.ServiceProvider.GetRequiredService<AppsService>();
        var fileStorage = scope.ServiceProvider.GetRequiredService<AppFileStorageService>();

        if (!Guid.TryParse(appId, out var appGuid))
        {
            return new McpToolResult { Success = false, Error = "Invalid app ID" };
        }

        var content = await fileStorage.ReadFileContentAsync(appGuid, filePath!);

        return new McpToolResult
        {
            Success = true,
            Content = content ?? $"File '{filePath}' not found or is empty"
        };
    }

    private async Task<McpToolResult> SaveAppFile(string appId, JsonElement arguments)
    {
        var filePath = arguments.GetProperty("filePath").GetString();
        var content = arguments.GetProperty("content").GetString();

        using var scope = _serviceProvider.CreateScope();
        var fileStorage = scope.ServiceProvider.GetRequiredService<AppFileStorageService>();

        if (!Guid.TryParse(appId, out var appGuid))
        {
            return new McpToolResult { Success = false, Error = "Invalid app ID" };
        }

        await fileStorage.WriteFileContentAsync(appGuid, filePath!, content!);

        return new McpToolResult
        {
            Success = true,
            Content = $"File '{filePath}' saved successfully"
        };
    }

    private async Task<McpToolResult> ListAppFiles(string appId)
    {
        using var scope = _serviceProvider.CreateScope();
        var fileStorage = scope.ServiceProvider.GetRequiredService<AppFileStorageService>();

        if (!Guid.TryParse(appId, out var appGuid))
        {
            return new McpToolResult { Success = false, Error = "Invalid app ID" };
        }

        var files = await fileStorage.ListFilesAsync(appGuid);
        var fileList = string.Join("\n", files.Select(f => $"- {f}"));

        return new McpToolResult
        {
            Success = true,
            Content = files.Any() ? $"Files in app:\n{fileList}" : "No files in app"
        };
    }

    private async Task<McpToolResult> GetLinkedSteps(string appId)
    {
        using var scope = _serviceProvider.CreateScope();
        var appStepsService = scope.ServiceProvider.GetRequiredService<AppStepsService>();

        if (!Guid.TryParse(appId, out var appGuid))
        {
            return new McpToolResult { Success = false, Error = "Invalid app ID" };
        }

        var steps = await appStepsService.GetAppStepsAsync(appGuid);
        if (!steps.Any())
        {
            return new McpToolResult
            {
                Success = true,
                Content = "No backend steps linked to this app"
            };
        }

        var stepList = string.Join("\n", steps.Select(s =>
            $"- {s.Name} (ID: {s.Id})\n  Display: {s.DisplayName}\n  Language: {s.Language}\n  Description: {s.Description ?? "No description"}"));

        return new McpToolResult
        {
            Success = true,
            Content = $"Backend steps:\n{stepList}"
        };
    }

    private async Task<McpToolResult> CreateBackendStep(string appId, JsonElement arguments)
    {
        var name = arguments.GetProperty("name").GetString();
        var displayName = arguments.GetProperty("displayName").GetString();
        var language = arguments.GetProperty("language").GetString();
        var code = arguments.GetProperty("code").GetString();
        var description = arguments.TryGetProperty("description", out var descProp)
            ? descProp.GetString()
            : null;

        using var scope = _serviceProvider.CreateScope();
        var appStepsService = scope.ServiceProvider.GetRequiredService<AppStepsService>();

        if (!Guid.TryParse(appId, out var appGuid))
        {
            return new McpToolResult { Success = false, Error = "Invalid app ID" };
        }

        // Get userId from context if available
        var userId = _context?.GetValueOrDefault("userId")?.ToString();
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var userGuid))
        {
            return new McpToolResult { Success = false, Error = "User ID is required" };
        }

        var step = await appStepsService.CreateAppStepAsync(
            appGuid,
            name!,
            displayName!,
            language!,
            code!,
            description,
            userGuid);

        return new McpToolResult
        {
            Success = true,
            Content = $"Created backend step '{displayName}' (ID: {step.Id}) and linked to app"
        };
    }

    private async Task<McpToolResult> ExecuteBackendStep(JsonElement arguments)
    {
        var stepIdStr = arguments.GetProperty("stepId").GetString();
        var inputs = arguments.TryGetProperty("inputs", out var inputsProp)
            ? inputsProp.Deserialize<Dictionary<string, object>>()
            : new Dictionary<string, object>();

        if (!Guid.TryParse(stepIdStr, out var stepId))
        {
            return new McpToolResult { Success = false, Error = "Invalid step ID" };
        }

        using var scope = _serviceProvider.CreateScope();
        var stepExecutionService = scope.ServiceProvider.GetRequiredService<AppsStepExecutionService>();

        try
        {
            var result = await stepExecutionService.ExecuteStepAsync(stepId, inputs!);
            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Success = false,
                Error = $"Step execution failed: {ex.Message}"
            };
        }
    }
}
