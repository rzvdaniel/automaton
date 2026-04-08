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
    public const string ReloadName = "ui_reload";
    public const string ClickName = "ui_click";
    public const string TypeName = "ui_type";
    public const string ReadTextName = "ui_read_text";
    public const string WaitForName = "ui_wait_for";
    public const string AssertName = "ui_assert";
    public const string GetPageSnapshotName = "ui_get_page_snapshot";
    public const string GetConsoleLogsName = "ui_get_console_logs";
    public const string SetBackendModeName = "ui_set_backend_mode";

    public const string EvaluateName = "ui_evaluate";
    public const string KeyboardName = "ui_keyboard";
    public const string GetAttributeName = "ui_get_attribute";

    public const string QueryAllName = "ui_query_all";
    public const string SelectName = "ui_select";
    public const string HoverName = "ui_hover";
    public const string ScrollToName = "ui_scroll_to";

    public const string CheckName = "ui_check";
    public const string GetCanvasDataName = "ui_get_canvas_data";
    public const string NetworkLogName = "ui_network_log";

    private static readonly HashSet<string> HandledTools =
    [
        NavigateName,
        ReloadName,
        ClickName,
        TypeName,
        ReadTextName,
        WaitForName,
        AssertName,
        GetPageSnapshotName,
        GetConsoleLogsName,
        SetBackendModeName,
        EvaluateName,
        KeyboardName,
        GetAttributeName,
        QueryAllName,
        SelectName,
        HoverName,
        ScrollToName,
        CheckName,
        GetCanvasDataName,
        NetworkLogName
    ];

    private readonly ILogger<UiAutomationTool> _logger;
    private readonly IUiAutomationActionAckService _ackService;

    public UiAutomationTool(
        ILogger<UiAutomationTool> logger,
        IUiAutomationActionAckService ackService)
    {
        _logger = logger;
        _ackService = ackService;
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
                Name = ReloadName,
                Capability = ToolCapability.Read,
                Description = "Reload the current preview page. Use this after editing files to pick up changes — replaces the common ui_navigate + ui_wait_for pattern for re-checks.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"waitForSelector":{"type":"string","description":"Optional CSS selector to wait for after reload (e.g. 'body', '#app')"},"timeoutMs":{"type":"number","description":"Max wait time in ms after reload (default 3000)"}},"required":[]}
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
                                Description = "Get captured browser console/runtime logs from the preview app. Use this after significant UI changes and before declaring completion. Prefer focused calls like { minLevel: 'warn', limit: 120 }. If logSummary.hasBlockingIssues is true, fix those errors before declaring completion.",
                                InputSchema = JsonDocument.Parse("""
                                        {
                                            "type": "object",
                                            "properties": {
                                                "minLevel": {
                                                    "type": "string",
                                                    "enum": ["debug", "info", "warn", "error"],
                                                    "description": "Minimum log level to return. Default: warn"
                                                },
                                                "limit": {
                                                    "type": "number",
                                                    "description": "Maximum number of log rows to return (20-300). Default: 120"
                                                },
                                                "maxMessageChars": {
                                                    "type": "number",
                                                    "description": "Maximum characters per log message before truncation (80-600). Default: 260"
                                                }
                                            }
                                        }
                                        """)
            },
            new()
            {
                Name = SetBackendModeName,
                Capability = ToolCapability.Read,
                Description = "Switch the preview backend between 'mock' (uses backend/mocks/*.json files, no runner needed) and 'real' (calls actual runners). Always call this with mode='mock' before running UI tests.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"mode":{"type":"string","enum":["mock","real"],"description":"'mock' to use local JSON mock files, 'real' to call actual runners"}},"required":["mode"]}
                    """)
            },

            new()
            {
                Name = EvaluateName,
                Capability = ToolCapability.Read,
                Description = "Evaluate a JavaScript expression inside the preview and return the result. Use this to inspect runtime state: window variables, component stores, counters, flags (e.g. 'window.score', 'document.querySelectorAll(\"li\").length').",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"expression":{"type":"string","description":"JS expression to evaluate — must be synchronous and JSON-serialisable (e.g. 'window.score', 'document.title')"}},"required":["expression"]}
                    """)
            },
            new()
            {
                Name = KeyboardName,
                Capability = ToolCapability.Read,
                Description = "Dispatch keyboard events (keydown + keypress + keyup) to an element or document.body. Use for games, modals, keyboard-driven UIs (e.g. ArrowUp, Enter, Escape, Space).",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"key":{"type":"string","description":"Key name per KeyboardEvent.key spec (e.g. 'Enter', 'Escape', 'ArrowUp', ' ')"},"selector":{"type":"string","description":"Optional CSS selector to focus before dispatching (default: document.body)"},"repeat":{"type":"number","description":"Number of times to dispatch (default 1, max 20)"},"ctrlKey":{"type":"boolean"},"shiftKey":{"type":"boolean"},"altKey":{"type":"boolean"}},"required":["key"]}
                    """)
            },
            new()
            {
                Name = GetAttributeName,
                Capability = ToolCapability.Read,
                Description = "Read one or more HTML attributes from an element. Use to verify disabled state, checked state, src, href, class, aria-label, data-* attributes.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"selector":{"type":"string","description":"CSS selector"},"attributes":{"type":"array","items":{"type":"string"},"description":"Attribute names to read (e.g. ['disabled','class','aria-label'])"}},"required":["selector","attributes"]}
                    """)
            },

            new()
            {
                Name = QueryAllName,
                Capability = ToolCapability.Read,
                Description = "Return the count of all elements matching a selector, and optionally their text content. Use to verify list/table row counts or enumerate repeated items.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"selector":{"type":"string"},"includeText":{"type":"boolean","description":"Include textContent of each matched element (default false)"},"limit":{"type":"number","description":"Max elements to return text for (default 20, max 50)"}},"required":["selector"]}
                    """)
            },
            new()
            {
                Name = SelectName,
                Capability = ToolCapability.Read,
                Description = "Set a <select> dropdown value and fire change events. Use instead of ui_type for native select elements.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"selector":{"type":"string","description":"CSS selector for the <select> element"},"value":{"type":"string","description":"Option value to select"}},"required":["selector","value"]}
                    """)
            },
            new()
            {
                Name = HoverName,
                Capability = ToolCapability.Read,
                Description = "Trigger mouseover/mouseenter/focus on an element. Use to reveal tooltips, dropdown menus, or hover-only UI content.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"selector":{"type":"string"}},"required":["selector"]}
                    """)
            },
            new()
            {
                Name = ScrollToName,
                Capability = ToolCapability.Read,
                Description = "Scroll an element into view. Use when the UI lazy-renders content below the fold or a sticky element blocks interaction.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"selector":{"type":"string"},"behavior":{"type":"string","enum":["smooth","instant"],"description":"Scroll behavior (default: instant)"}},"required":["selector"]}
                    """)
            },

            new()
            {
                Name = CheckName,
                Capability = ToolCapability.Read,
                Description = "Set the checked state of a checkbox or radio button and fire change events.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"selector":{"type":"string"},"checked":{"type":"boolean","description":"Target state — true to check, false to uncheck (default true)"}},"required":["selector"]}
                    """)
            },
            new()
            {
                Name = GetCanvasDataName,
                Capability = ToolCapability.Read,
                Description = "Sample pixel data from a <canvas> element to confirm something rendered. Returns hasNonBlackPixels and average alpha. Works for both 2D and WebGL/Three.js canvases.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"selector":{"type":"string","description":"Canvas CSS selector (default: 'canvas')"},"sampleSize":{"type":"number","description":"NxN grid of sample points (default 4, max 16)"}}}
                    """)
            },
            new()
            {
                Name = NetworkLogName,
                Capability = ToolCapability.Read,
                Description = "Return captured fetch/XHR calls made by the preview app. Use to verify API calls were made and check response status codes.",
                InputSchema = JsonDocument.Parse("""
                    {"type":"object","properties":{"limit":{"type":"number","description":"Max entries to return, most recent first (default 20, max 100)"},"urlPattern":{"type":"string","description":"Optional substring to filter by URL"}}}
                    """)
            }
        };

        return Task.FromResult<IEnumerable<McpTool>>(tools);
    }

    public bool CanHandle(string toolName) => HandledTools.Contains(toolName);

    public async Task<McpToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, Dictionary<string, object>? context)
    {
        try
        {
            var toolCallId = context?.TryGetValue("toolCallId", out var toolCallIdObj) == true
                ? toolCallIdObj?.ToString()
                : null;
            if (string.IsNullOrWhiteSpace(toolCallId))
                toolCallId = Guid.NewGuid().ToString("N");

            var timeoutMs = context?.TryGetValue("uiActionTimeoutMs", out var timeoutObj) == true
                && int.TryParse(timeoutObj?.ToString(), out var parsedTimeout)
                    ? Math.Clamp(parsedTimeout, 2000, 45000)
                    : 15000;

            var envelope = new
            {
                clientExecutionRequired = true,
                bridgeProtocol = "ui-action-ack-v1",
                actionId = toolCallId,
                action = toolName,
                arguments = JsonSerializer.Deserialize<object>(arguments.GetRawText()),
                timeoutMs,
                note = "Execute this action in the App Editor preview bridge and report result via ui-action ack endpoint."
            };

            var ackResult = await _ackService.WaitForResultAsync(
                toolCallId,
                TimeSpan.FromMilliseconds(timeoutMs));

            if (!string.IsNullOrWhiteSpace(ackResult))
            {
                return new McpToolResult
                {
                    Success = true,
                    Content = ackResult
                };
            }

            // Fallback to envelope content so existing UI flow still works if ack is unavailable.
            return new McpToolResult
            {
                Success = true,
                Content = JsonSerializer.Serialize(envelope)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare UI automation tool response for {ToolName}", toolName);
            return new McpToolResult
            {
                Success = false,
                Error = $"Failed to prepare UI action envelope: {ex.Message}"
            };
        }
    }
}

