using System.Text.Json;
using Automaton.Mcp.Models;
using Microsoft.Extensions.Logging;

namespace Automaton.Mcp.Tools;

/// <summary>
/// UI automation MCP tools for preview interaction.
/// Backend returns structured client-action envelopes that the frontend executes in the App Editor preview.
/// </summary>
public class UiAutomationTool : IMcpTool
{
    public const string NavigateName = "ui_navigate";
    public const string ClickName = "ui_click";
    public const string TypeName = "ui_type";
    public const string ReadTextName = "ui_read_text";
    public const string WaitForName = "ui_wait_for";
    public const string AssertName = "ui_assert";
    public const string GetPageSnapshotName = "ui_get_page_snapshot";
    public const string GetConsoleLogsName = "ui_get_console_logs";

    private static readonly HashSet<string> HandledTools =
    [
        NavigateName,
        ClickName,
        TypeName,
        ReadTextName,
        WaitForName,
        AssertName,
        GetPageSnapshotName,
        GetConsoleLogsName
    ];

    private readonly ILogger<UiAutomationTool> _logger;

    public UiAutomationTool(ILogger<UiAutomationTool> logger)
    {
        _logger = logger;
    }

    public Task<IEnumerable<McpTool>> GetToolsAsync()
    {
        var tools = new List<McpTool>
        {
            new()
            {
                Name = NavigateName,
                Capability = ToolCapability.Read,
                Description = "Navigate the preview app to a URL/hash route in the embedded App Editor preview.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"url":{"type":"string","description":"Absolute or relative URL/hash route"}},"required":["url"]}
                    """)
            },
            new()
            {
                Name = ClickName,
                Capability = ToolCapability.Read,
                Description = "Click an element in the preview app by CSS selector.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"selector":{"type":"string","description":"CSS selector to click"}},"required":["selector"]}
                    """)
            },
            new()
            {
                Name = TypeName,
                Capability = ToolCapability.Read,
                Description = "Type into an input/textarea element in the preview app by CSS selector.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"selector":{"type":"string"},"text":{"type":"string"}},"required":["selector","text"]}
                    """)
            },
            new()
            {
                Name = ReadTextName,
                Capability = ToolCapability.Read,
                Description = "Read text content from an element in the preview app by CSS selector.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"selector":{"type":"string"}},"required":["selector"]}
                    """)
            },
            new()
            {
                Name = WaitForName,
                Capability = ToolCapability.Read,
                Description = "Wait for an element to appear in the preview app.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"selector":{"type":"string"},"timeoutMs":{"type":"number","description":"Max wait time in ms (default 3000)"},"intervalMs":{"type":"number","description":"Polling interval in ms (default 100)"}},"required":["selector"]}
                    """)
            },
            new()
            {
                Name = AssertName,
                Capability = ToolCapability.Read,
                Description = "Assert preview conditions by selector and optional text expectations.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"selector":{"type":"string"},"exists":{"type":"boolean","description":"Whether selector should exist (default true)"},"containsText":{"type":"string","description":"Expected substring in element text"},"equalsText":{"type":"string","description":"Expected exact element text"}},"required":["selector"]}
                    """)
            },
            new()
            {
                Name = GetPageSnapshotName,
                Capability = ToolCapability.Read,
                Description = "Get a lightweight snapshot of the preview page (title, url, body excerpt).",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{"maxChars":{"type":"number","description":"Body text max length (default 1000)"}}}""")
            },
            new()
            {
                Name = GetConsoleLogsName,
                Capability = ToolCapability.Read,
                Description = "Get captured browser console/runtime logs from the preview app. Use this after significant UI changes and before declaring completion; inspect returned logs for issues and fix them.",
                InputSchema = JsonDocument.Parse("""{"type":"object","properties":{}}""")
            }
        };

        return Task.FromResult<IEnumerable<McpTool>>(tools);
    }

    public bool CanHandle(string toolName) => HandledTools.Contains(toolName);

    public Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var envelope = new
            {
                clientExecutionRequired = true,
                action = toolName,
                arguments = JsonSerializer.Deserialize<object>(arguments.GetRawText()),
                note = "Execute this action in the App Editor preview bridge and report results in live UI logs."
            };

            return Task.FromResult(new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(envelope)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare UI automation tool response for {ToolName}", toolName);
            return Task.FromResult(new McpToolResult
            {
                Success = false,
                Error = $"Failed to prepare UI action envelope: {ex.Message}"
            });
        }
    }
}

