using System.Text.Json;
using Automaton.Mcp.Models;
using Automaton.Mcp.Services;
using Microsoft.EntityFrameworkCore;

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
                Name = "execute_backend_step",
                Description = "Execute a backend step on the app's configured runner to test it. Requires a runner to be assigned to the app.",
                InputSchema = JsonDocument.Parse(@"{
                    ""type"": ""object"",
                    ""properties"": {
                        ""stepId"": {
                            ""type"": ""string"",
                            ""description"": ""ID or name of the step to execute""
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
            if (string.IsNullOrEmpty(appId))
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "App ID is required"
                };
            }

            if (!Guid.TryParse(appId, out var appGuid))
            {
                return new McpToolResult { Success = false, Error = "Invalid app ID format" };
            }

            // Get userId from context
            var userIdStr = _context?.GetValueOrDefault("userId")?.ToString();
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
            {
                return new McpToolResult { Success = false, Error = "User ID is required in context" };
            }

            return toolName switch
            {
                "get_app_file" => await GetAppFile(userId, appGuid, arguments),
                "save_app_file" => await SaveAppFile(userId, appGuid, arguments),
                "list_app_files" => await ListAppFiles(userId, appGuid),
                "get_linked_steps" => await GetLinkedSteps(appGuid),
                "execute_backend_step" => await ExecuteBackendStep(appGuid, arguments),
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

    private async Task<McpToolResult> GetAppFile(Guid userId, Guid appId, JsonElement arguments)
    {
        var filePath = arguments.GetProperty("filePath").GetString();
        if (string.IsNullOrEmpty(filePath))
        {
            return new McpToolResult { Success = false, Error = "filePath is required" };
        }

        using var scope = _serviceProvider.CreateScope();
        var fileStorage = scope.ServiceProvider.GetRequiredService<AppFileStorageService>();

        var content = await fileStorage.ReadAppFileAsync(userId, appId, filePath);

        return new McpToolResult
        {
            Success = true,
            Content = content ?? $"File '{filePath}' not found or is empty"
        };
    }

    private async Task<McpToolResult> SaveAppFile(Guid userId, Guid appId, JsonElement arguments)
    {
        var filePath = arguments.GetProperty("filePath").GetString();
        var content = arguments.GetProperty("content").GetString();

        if (string.IsNullOrEmpty(filePath))
        {
            return new McpToolResult { Success = false, Error = "filePath is required" };
        }

        using var scope = _serviceProvider.CreateScope();
        var fileStorage = scope.ServiceProvider.GetRequiredService<AppFileStorageService>();

        await fileStorage.WriteAppFileAsync(userId, appId, filePath, content ?? "");

        return new McpToolResult
        {
            Success = true,
            Content = $"File '{filePath}' saved successfully"
        };
    }

    private async Task<McpToolResult> ListAppFiles(Guid userId, Guid appId)
    {
        using var scope = _serviceProvider.CreateScope();
        var fileStorage = scope.ServiceProvider.GetRequiredService<AppFileStorageService>();

        var files = await fileStorage.GetAppFilesAsync(userId, appId);
        var fileList = string.Join("\n", files.Select(f => $"- {f}"));

        return new McpToolResult
        {
            Success = true,
            Content = files.Any() ? $"Files in app:\n{fileList}" : "No files in app"
        };
    }

    private async Task<McpToolResult> GetLinkedSteps(Guid appId)
    {
        using var scope = _serviceProvider.CreateScope();
        var stepExecutionService = scope.ServiceProvider.GetRequiredService<AppsStepExecutionService>();

        var steps = await stepExecutionService.GetLinkedStepsAsync(appId);
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

    private async Task<McpToolResult> ExecuteBackendStep(Guid appId, JsonElement arguments)
    {
        var stepIdentifier = arguments.GetProperty("stepId").GetString();
        var inputs = arguments.TryGetProperty("inputs", out var inputsProp)
            ? inputsProp.Deserialize<Dictionary<string, string>>()
            : new Dictionary<string, string>();

        if (string.IsNullOrEmpty(stepIdentifier))
        {
            return new McpToolResult { Success = false, Error = "stepId is required" };
        }

        using var scope = _serviceProvider.CreateScope();
        var appsService = scope.ServiceProvider.GetRequiredService<AppsService>();
        var stepExecutionService = scope.ServiceProvider.GetRequiredService<AppsStepExecutionService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<Data.ApplicationDbContext>();

        try
        {
            // Get app info using the service method that returns PublishedAppInfo
            var appInfo = await appsService.GetAppInfoByIdAsync(appId);

            if (appInfo == null)
            {
                return new McpToolResult { Success = false, Error = "App not found" };
            }

            // Check if app has a runner configured
            if (!appInfo.ExecutionRunnerId.HasValue)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "No runner assigned to this app. Please assign a runner in the app settings to enable step execution."
                };
            }

            // Check if the runner is online
            var runner = await dbContext.Runners
                .FirstOrDefaultAsync(r => r.Id == appInfo.ExecutionRunnerId.Value);

            if (runner == null)
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = "Configured runner not found. Please check the app settings."
                };
            }

            if (string.IsNullOrEmpty(runner.ConnectionId))
            {
                return new McpToolResult
                {
                    Success = false,
                    Error = $"Runner '{runner.Name}' is offline. Please ensure the runner is connected before testing steps."
                };
            }

            // Execute the step on the runner
            var result = await stepExecutionService.ExecuteStepForAppAsync(
                appInfo,
                stepIdentifier,
                inputs ?? new Dictionary<string, string>(),
                null, // Use app's configured runner
                CancellationToken.None);

            if (result.Success)
            {
                return new McpToolResult
                {
                    Success = true,
                    Content = JsonSerializer.Serialize(new
                    {
                        success = true,
                        message = $"Step executed successfully on runner '{runner.Name}'",
                        executionTime = $"{(result.CompletedAt - result.ExecutedAt).TotalMilliseconds:F0}ms",
                        outputVariables = result.OutputVariables,
                        logs = result.Logs?.Take(10) // Limit logs for token efficiency
                    }, new JsonSerializerOptions { WriteIndented = true })
                };
            }
            else
            {
                return new McpToolResult
                {
                    Success = false,
                    Content = JsonSerializer.Serialize(new
                    {
                        success = false,
                        error = result.Error,
                        logs = result.Logs
                    }, new JsonSerializerOptions { WriteIndented = true }),
                    Error = result.Error
                };
            }
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
