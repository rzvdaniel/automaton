using Automaton.Mcp.Tools;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Studio.Server.Controllers;

/// <summary>
/// App-agnostic MCP (Model Context Protocol) server for Claude Code, Cursor, Codex, and other
/// AI coding tools. Implements JSON-RPC 2.0. Handles any number of apps — the AI reads the
/// target appId from a local <c>automaton.json</c> file written by the <c>create_app</c> tool.
///
/// ## Connecting (one-time setup, any number of apps)
///
///   Add to .vscode/mcp.json:
///   {
///     "servers": {
///       "automaton": {
///         "type": "http",
///         "url": "https://your-server/api/mcp",
///         "headers": { "X-API-Key": "ak_execute_xxx" }
///       }
///     }
///   }
///
/// ## Available Tools (read / discovery only — deploy via automaton CLI)
///   create_app   — Create a new app; returns appId to store in automaton.json
///   read_file    — Read a file (supports line ranges)
///   list_files   — List editable app source files
///   list_runners — List the user's connected runners
///
/// For file writes and deployment use the automaton CLI:
///   automaton deploy   — build, upload, deploy backend
///   automaton pull     — sync server files locally
///   automaton publish  — save a version snapshot
///   automaton runners  — list runners
///
/// ## Auth
///   X-API-Key: ak_execute_xxx  (header)
///   ?api_key=ak_execute_xxx    (query parameter)
///   Authorization: Bearer ak_execute_xxx  (Bearer with ak_ prefix)
/// </summary>
[ApiController]
[Route("api/mcp")]
[Authorize(Policy = "AllowApiKey")]
public class ExternalMcpController : ControllerBase
{
    // Edit these files to update the prompt without recompiling.
    private static readonly string[] InstructionFiles =
    [
        "SystemPrompts/ExternalMcpInstructions.md",
    ];

    private readonly AppsService _appsService;
    private readonly FileIoTool _fileIoTool;
    private readonly UserContextService _userContext;
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ExternalMcpController> _logger;

    public ExternalMcpController(
        AppsService appsService,
        FileIoTool fileIoTool,
        UserContextService userContext,
        ApplicationDbContext db,
        IWebHostEnvironment env,
        ILogger<ExternalMcpController> logger)
    {
        _appsService = appsService;
        _fileIoTool = fileIoTool;
        _userContext = userContext;
        _db = db;
        _env = env;
        _logger = logger;
    }

    private string LoadInstructions(string server, string apiKey)
    {
        var parts = new List<string>();

        foreach (var relativePath in InstructionFiles)
        {
            var path = Path.Combine(_env.ContentRootPath, relativePath);
            if (System.IO.File.Exists(path))
            {
                parts.Add(System.IO.File.ReadAllText(path));
            }
            else
            {
                _logger.LogWarning("MCP instructions file not found at {Path}.", path);
            }
        }

        if (parts.Count == 0)
            return string.Empty;

        return string.Join("\n\n", parts)
            .Replace("{server}", server)
            .Replace("{apiKey}", apiKey);
    }

    /// <summary>
    /// Accepts MCP JSON-RPC 2.0 requests (single or batch).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Handle([FromBody] JsonElement body, CancellationToken ct)
    {
        var userId = _userContext.GetUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        // Batch request
        if (body.ValueKind == JsonValueKind.Array)
        {
            var results = new List<object>();
            foreach (var item in body.EnumerateArray())
            {
                var r = await ProcessOneRequest(item, userId, ct);
                if (r != null) results.Add(r);
            }
            return Ok(results);
        }

        // Single request
        var response = await ProcessOneRequest(body, userId, ct);
        if (response == null) return NoContent(); // notification — no response required
        return Ok(response);
    }

    // ── JSON-RPC dispatcher ───────────────────────────────────────────────────

    private async Task<object?> ProcessOneRequest(JsonElement req, Guid userId, CancellationToken ct)
    {
        var method = req.TryGetProperty("method", out var m) ? m.GetString() : null;
        var isNotification = !req.TryGetProperty("id", out var idProp);

        if (isNotification)
            return null;

        object? id = idProp.ValueKind switch
        {
            JsonValueKind.Number => idProp.TryGetInt64(out var n) ? (object)n : idProp.GetDecimal(),
            JsonValueKind.String => idProp.GetString(),
            _ => null
        };

        if (method == null)
            return RpcError(id, -32600, "Invalid request: missing 'method'");

        try
        {
            var result = method switch
            {
                "initialize" => HandleInitialize(req),
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleToolCall(req, userId, ct),
                "ping"       => new { },
                _ => throw new NotSupportedException($"Method not found: {method}")
            };
            return new { jsonrpc = "2.0", id, result };
        }
        catch (NotSupportedException ex)
        {
            return RpcError(id, -32601, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP request failed: method={Method}", method);
            return RpcError(id, -32603, ex.Message);
        }
    }

    // ── Method handlers ───────────────────────────────────────────────────────

    private object HandleInitialize(JsonElement req)
    {
        var protocolVersion = "2024-11-05";
        if (req.TryGetProperty("params", out var p) && p.TryGetProperty("protocolVersion", out var pv))
            protocolVersion = pv.GetString() ?? protocolVersion;

        var serverUrl = $"{Request.Scheme}://{Request.Host}";
        var apiKey    = Request.Headers["X-API-Key"].FirstOrDefault()
                     ?? ExtractBearerApiKey(Request.Headers["Authorization"].FirstOrDefault());

        return new
        {
            protocolVersion,
            capabilities = new { tools = new { } },
            serverInfo = new { name = "automaton-mcp", version = "1.0.0" },
            instructions = LoadInstructions(serverUrl, apiKey ?? "{apiKey}")
        };
    }

    /// Extracts the key from "Bearer ak_xxx" — only if it starts with "ak_" (our API key prefix).
    private static string? ExtractBearerApiKey(string? header)
    {
        if (header != null && header.StartsWith("Bearer ak_", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..];
        return null;
    }

    private static object HandleToolsList() => new
    {
        tools = new object[]
        {
            ToolDef("create_app",
                "Create a new Automaton app. Returns the appId. Immediately write the returned appId to 'automaton.json' in the project root using your local file tools so it persists across sessions.",
                Props(
                    Prop("name", "string", "Human-readable name for the app")),
                ["name"]),

            ToolDef("read_file",
                "Read a file (or a line range within it) from the Automaton app. Always returns a header showing total lines.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        appId     = new { type = "string",  description = "App ID — read from automaton.json" },
                        path      = new { type = "string",  description = "Relative file path" },
                        startLine = new { type = "integer", description = "1-based first line to return (default: 1)" },
                        endLine   = new { type = "integer", description = "1-based last line to return, inclusive (default: end of file)" }
                    },
                    required = new[] { "appId", "path" }
                }),

            ToolDef("list_files",
                "List editable source files currently stored for the Automaton app.",
                Props(
                    Prop("appId", "string", "App ID — read from automaton.json")),
                ["appId"]),

            ToolDef("list_runners",
                "List the runners available to the current user.",
                new { type = "object", properties = new { }, required = Array.Empty<string>() })
        }
    };

    private async Task<object> HandleToolCall(JsonElement req, Guid userId, CancellationToken ct)
    {
        if (!req.TryGetProperty("params", out var p))
            throw new ArgumentException("Missing 'params' in tools/call request");

        var name = p.GetProperty("name").GetString()
            ?? throw new ArgumentException("Missing tool name");
        var args = p.TryGetProperty("arguments", out var a) ? a : default;

        try
        {
            var text = name switch
            {
                "create_app"   => await ExecCreateApp(args, userId),
                "read_file" or
                "list_files"   => await ExecFileTool(name, args),
                "list_runners" => await ExecListRunners(userId),
                _ => throw new InvalidOperationException($"Unknown tool: {name}")
            };
            return ToolResult(text);
        }
        catch (Exception ex)
        {
            return ToolError(ex.Message);
        }
    }

    // ── Tool implementations ──────────────────────────────────────────────────

    private async Task<string> ExecCreateApp(JsonElement args, Guid userId)
    {
        var name   = RequireString(args, "name");
        var appId  = await _appsService.Create(new NewApp { Name = name });
        var server = $"{Request.Scheme}://{Request.Host}";

        // Also fetch available runners so the AI can prompt the user immediately.
        var runners = await _db.Runners
            .Where(r => r.RunnerUsers.Any(ru => ru.UserId == userId))
            .OrderBy(r => r.Name)
            .Select(r => new { r.Name, Connected = r.ConnectionId != null })
            .ToListAsync();

        var runnerSection = runners.Count == 0
            ? "No runners are registered on this account. If the app has a Python backend, register a runner first."
            : "Available runners:\n" + string.Join("\n", runners.Select(r =>
                $"- {r.Name} ({(r.Connected ? "connected" : "offline")})"));

        return $$"""
            App '{{name}}' created with ID: {{appId}}

            IMPORTANT: Overwrite (replace entirely) 'automaton.json' in the project root with ONLY this content — do not append to any existing content:
            {
              "appId": "{{appId}}",
              "name": "{{name}}"
            }
            The file must contain exactly one JSON object. Delete any previous content.

            {{runnerSection}}

            NEXT STEPS:
            - If the app needs a Python backend, you will need to assign a runner before deploying.
            - Run 'automaton set-runner <runnerName>' from the CLI to assign a runner.
            - Run 'automaton deploy' to upload files and deploy the backend.
            """;
    }

    private async Task<string> ExecFileTool(string toolName, JsonElement args)
    {
        var result = await _fileIoTool.ExecuteToolAsync(toolName, args, null);
        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "Tool execution failed");
        return result.Content ?? string.Empty;
    }

    private async Task<string> ExecListRunners(Guid userId)
    {
        var runners = await _db.Runners
            .Where(r => r.RunnerUsers.Any(ru => ru.UserId == userId))
            .OrderBy(r => r.Name)
            .Select(r => new { r.Name, Connected = r.ConnectionId != null })
            .ToListAsync();

        if (!runners.Any())
            return "No runners found. Register a runner first via the Automaton Studio Runners page.";

        var lines = runners.Select(r => $"- {r.Name} ({(r.Connected ? "connected" : "offline")})");
        return "Available runners:\n" + string.Join("\n", lines);
    }

    // ExecSetAppRunner, ExecDeployBackend, ExecPublishApp removed.
    // Use the automaton CLI for deployment: automaton deploy / automaton set-runner / automaton publish.

    // ── Response builders ─────────────────────────────────────────────────────

    private static object ToolResult(string text) => new
    {
        content = new[] { new { type = "text", text } }
    };

    private static object ToolError(string message) => new
    {
        content = new[] { new { type = "text", text = "Error: " + message } },
        isError = true
    };

    private static object RpcError(object? id, int code, string message) =>
        new { jsonrpc = "2.0", id, error = new { code, message } };

    // ── Tool schema helpers ───────────────────────────────────────────────────

    private static object ToolDef(string name, string description, object inputSchema) =>
        new { name, description, inputSchema };

    private static object ToolDef(string name, string description, object properties, string[] required) =>
        new { name, description, inputSchema = new { type = "object", properties, required } };

    private static object Props(params object[] props) => MergeProps(props);

    private static object Prop(string name, string type, string description) =>
        new { name, schema = new { type, description } };

    private static Dictionary<string, object> MergeProps(object[] props)
    {
        var dict = new Dictionary<string, object>();
        foreach (dynamic p in props)
            dict[p.name] = p.schema;
        return dict;
    }

    private static Guid RequireAppId(JsonElement args)
    {
        var raw = RequireString(args, "appId");
        if (!Guid.TryParse(raw, out var appId))
            throw new ArgumentException($"'appId' is not a valid GUID: {raw}");
        return appId;
    }

    private static string RequireString(JsonElement args, string key)
    {
        if (!args.TryGetProperty(key, out var val) || val.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"Missing required argument '{key}'");
        return val.GetString()!;
    }
}
