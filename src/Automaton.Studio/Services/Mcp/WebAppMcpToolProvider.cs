using System.Text.Json;
using Microsoft.Extensions.Logging;
using Automaton.Mcp.Services;
using Automaton.Mcp.Models;

namespace Automaton.Studio.Services.Mcp;

/// <summary>
/// Implementation of MCP tools for WebApp HTML/CSS/JavaScript editing.
/// Separate from StudioMcpToolProvider to maintain clear separation of concerns.
/// </summary>
public class WebAppMcpToolProvider : IMcpToolProvider
{
    private readonly ILogger<WebAppMcpToolProvider> _logger;
    private readonly ICurrentWebAppContextService _webAppContextService;
    private readonly AiCodeCleanupService _codeCleanupService;
    private readonly Dictionary<string, WebAppContext> _appContexts = new();

    public WebAppMcpToolProvider(
        ILogger<WebAppMcpToolProvider> logger,
        ICurrentWebAppContextService webAppContextService,
        AiCodeCleanupService codeCleanupService)
    {
        _logger = logger;
        _webAppContextService = webAppContextService;
        _codeCleanupService = codeCleanupService;
    }

    public Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        var tools = new List<McpTool>
        {
            new McpTool
            {
                Name = "get_current_code",
                Description = "Get the current HTML/CSS/JavaScript code from the web app editor",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "appId": {
                            "type": "string",
                            "description": "The ID of the web app to get code from"
                        }
                    },
                    "required": ["appId"]
                }
                """)
            },
            new McpTool
            {
                Name = "save_code",
                Description = "Save generated HTML/CSS/JavaScript code to the web app editor",
                InputSchema = JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "appId": {
                            "type": "string",
                            "description": "The ID of the web app to save code to"
                        },
                        "code": {
                            "type": "string",
                            "description": "The HTML/CSS/JavaScript code to save"
                        }
                    },
                    "required": ["appId", "code"]
                }
                """)
            }
        };

        return Task.FromResult<IEnumerable<McpTool>>(tools);
    }

    public async Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, string? contextId = null, Dictionary<string, object>? context = null)
    {
        try
        {
            return toolName switch
            {
                "get_current_code" => await GetCurrentCodeAsync(arguments, contextId, context),
                "save_code" => await SaveCodeAsync(arguments, contextId, context),
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

    private async Task<McpToolResult> GetCurrentCodeAsync(JsonElement arguments, string? contextId, Dictionary<string, object>? context)
    {
        var appId = arguments.TryGetProperty("appId", out var appIdProp) 
            ? appIdProp.GetString() 
            : contextId ?? _webAppContextService.GetCurrentWebAppId();
        
        if (string.IsNullOrEmpty(appId))
        {
            return new McpToolResult { Success = false, Error = "App ID is required" };
        }

        try
        {
            string currentCode;
            
            // Check if context has a getCurrentCode function
            if (context != null && context.TryGetValue("getCurrentCode", out var getCurrentCodeObj))
            {
                if (getCurrentCodeObj is Func<Task<string>> getCurrentCodeFunc)
                {
                    currentCode = await getCurrentCodeFunc();
                }
                else
                {
                    currentCode = await _webAppContextService.GetCurrentCodeAsync();
                }
            }
            else
            {
                currentCode = await _webAppContextService.GetCurrentCodeAsync();
            }
            
            if (string.IsNullOrEmpty(currentCode))
            {
                return new McpToolResult 
                { 
                    Success = true, 
                    Content = "<!-- No code found. Start with a new HTML template -->\n<!DOCTYPE html>\n<html>\n<head>\n    <title>New App</title>\n</head>\n<body>\n    <!-- Add your content here -->\n</body>\n</html>" 
                };
            }

            // Return the actual code with metadata
            var result = new
            {
                contextId = appId,
                language = "html",
                code = currentCode,
                codeAnalysis = new
                {
                    linesOfCode = currentCode.Split('\n').Length,
                    hasStyleTag = currentCode.Contains("<style", StringComparison.OrdinalIgnoreCase),
                    hasScriptTag = currentCode.Contains("<script", StringComparison.OrdinalIgnoreCase),
                    hasDoctype = currentCode.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
                },
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
            _logger.LogError(ex, "Error getting current code for web app {AppId}", appId);
            return new McpToolResult 
            { 
                Success = false, 
                Error = $"Failed to get current code: {ex.Message}" 
            };
        }
    }

    private async Task<McpToolResult> SaveCodeAsync(JsonElement arguments, string? contextId, Dictionary<string, object>? context)
    {
        var appId = arguments.TryGetProperty("appId", out var appIdProp)
            ? appIdProp.GetString()
            : contextId ?? _webAppContextService.GetCurrentWebAppId();
        var rawCode = arguments.GetProperty("code").GetString();

        if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(rawCode))
        {
            return new McpToolResult { Success = false, Error = "App ID and code are required" };
        }

        try
        {
            // For HTML, we may want to clean up markdown artifacts but be careful with HTML structure
            var cleanedCode = CleanupHtmlCode(rawCode);

            if (string.IsNullOrWhiteSpace(cleanedCode))
            {
                _logger.LogWarning("Code cleanup resulted in empty code for web app {AppId}", appId);
                return new McpToolResult
                {
                    Success = false,
                    Error = "Code extraction failed - no valid HTML found after cleanup"
                };
            }

            // Log what was cleaned
            if (rawCode != cleanedCode)
            {
                _logger.LogInformation("Code cleaned for web app {AppId}: {OriginalLength} -> {CleanedLength} chars",
                    appId, rawCode.Length, cleanedCode.Length);
            }

            // Save the cleaned code to the web app context
            await _webAppContextService.SetCodeAsync(cleanedCode);

            // Also store in our local context for future reference
            _appContexts[appId] = new WebAppContext { Code = cleanedCode };

            _logger.LogInformation("Code saved for web app {AppId} ({Length} characters, {Lines} lines)",
                appId, cleanedCode.Length, cleanedCode.Split('\n').Length);

            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(new
                {
                    message = "Code saved successfully to App Designer",
                    appId = appId,
                    language = "html",
                    linesOfCode = cleanedCode.Split('\n').Length,
                    wasCleanedUp = rawCode != cleanedCode,
                    note = "Code has been extracted and cleaned. Visible in editor now."
                }, new JsonSerializerOptions { WriteIndented = true })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving code for web app {AppId}", appId);
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to save code: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Clean up HTML code by removing markdown artifacts while preserving HTML structure
    /// </summary>
    private string CleanupHtmlCode(string rawCode)
    {
        if (string.IsNullOrEmpty(rawCode))
            return rawCode;

        var code = rawCode.Trim();

        // Remove markdown code block markers if present
        if (code.StartsWith("```html", StringComparison.OrdinalIgnoreCase))
        {
            code = code.Substring(7).TrimStart();
        }
        else if (code.StartsWith("```"))
        {
            code = code.Substring(3).TrimStart();
        }

        if (code.EndsWith("```"))
        {
            code = code.Substring(0, code.Length - 3).TrimEnd();
        }

        return code;
    }
}

/// <summary>
/// Context for tracking web app state
/// </summary>
public class WebAppContext
{
    public string? Code { get; set; }
}
