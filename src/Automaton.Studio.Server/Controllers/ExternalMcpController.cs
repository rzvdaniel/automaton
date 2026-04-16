using Automaton.Mcp.Tools;
using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Enums;
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
/// ## Available Tools
///   create_app      — Create a new app; returns appId to store in automaton.json
///   write_file      — Write (overwrite) a file
///   read_file       — Read a file (supports line ranges)
///   list_files      — List all app files
///   delete_file     — Delete one or more files
///   delete_folder   — Delete a folder and all its contents
///   rename_file     — Rename or move a file
///   rename_folder   — Rename or move a folder
///   list_runners    — List the user's connected runners
///   set_app_runner  — Assign a runner to the app before deploying
///   deploy_backend  — Deploy backend Python scripts to the configured runner
///   publish_app     — Save a version snapshot of the app's current files
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
        "SystemPrompts/ExternalMcpBackendInstructions.md",
        "SystemPrompts/ExternalMcpUiInstructions.md",
    ];

    private readonly AppsService _appsService;
    private readonly FileIoTool _fileIoTool;
    private readonly UserContextService _userContext;
    private readonly AppsControllerExecutionService _deployService;
    private readonly AppVersionService _appVersionService;
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ExternalMcpController> _logger;

    public ExternalMcpController(
        AppsService appsService,
        FileIoTool fileIoTool,
        UserContextService userContext,
        AppsControllerExecutionService deployService,
        AppVersionService appVersionService,
        ApplicationDbContext db,
        IWebHostEnvironment env,
        ILogger<ExternalMcpController> logger)
    {
        _appsService = appsService;
        _fileIoTool = fileIoTool;
        _userContext = userContext;
        _deployService = deployService;
        _appVersionService = appVersionService;
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

            ToolDef("write_file",
                "Write a file to the Automaton app with concrete content. Overwrites the file if it already exists — never delete or empty a file before writing it. Creates parent directories if needed.",
                Props(
                    Prop("appId",   "string", "App ID — read from automaton.json"),
                    Prop("path",    "string", "Relative file path, e.g. 'index.html' or 'backend/todos.py'"),
                    Prop("content", "string", "Full file content")),
                ["appId", "path", "content"]),

            ToolDef("read_file",
                "Read a file (or a line range within it). Always returns a header showing total lines.",
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
                "List all files currently in the Automaton app.",
                Props(
                    Prop("appId", "string", "App ID — read from automaton.json")),
                ["appId"]),

            ToolDef("delete_file",
                "Delete one or more files from the Automaton app. Only use when explicitly asked — never call this to replace file content (use write_file instead, which overwrites).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        appId  = new { type = "string", description = "App ID — read from automaton.json" },
                        paths  = new { type = "array",  items = new { type = "string" }, description = "File paths to delete" }
                    },
                    required = new[] { "appId", "paths" }
                }),

            ToolDef("delete_folder",
                "Delete a folder and all its contents from the Automaton app. Only use when explicitly asked.",
                Props(
                    Prop("appId", "string", "App ID — read from automaton.json"),
                    Prop("path",  "string", "Relative folder path to delete")),
                ["appId", "path"]),

            ToolDef("rename_file",
                "Rename or move a file within the Automaton app.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        appId     = new { type = "string",  description = "App ID — read from automaton.json" },
                        fromPath  = new { type = "string",  description = "Current relative file path" },
                        toPath    = new { type = "string",  description = "New relative file path" },
                        overwrite = new { type = "boolean", description = "Set true to replace destination file if it exists" }
                    },
                    required = new[] { "appId", "fromPath", "toPath" }
                }),

            ToolDef("rename_folder",
                "Rename or move a folder within the Automaton app.",
                Props(
                    Prop("appId",    "string", "App ID — read from automaton.json"),
                    Prop("fromPath", "string", "Current relative folder path"),
                    Prop("toPath",   "string", "New relative folder path")),
                ["appId", "fromPath", "toPath"]),

            ToolDef("list_runners",
                "List the runners available to the current user. Call this before set_app_runner to discover runner names.",
                new { type = "object", properties = new { }, required = Array.Empty<string>() }),

            ToolDef("set_app_runner",
                "Assign a runner to the app. Must be called before deploy_backend if no runner is assigned yet.",
                Props(
                    Prop("appId",      "string", "App ID — read from automaton.json"),
                    Prop("runnerName", "string", "Runner name — from list_runners")),
                ["appId", "runnerName"]),

            ToolDef("deploy_backend",
                "Deploy Python backend scripts to the app's configured runner. Call this after writing backend/*.py files.",
                Props(
                    Prop("appId", "string", "App ID — read from automaton.json")),
                ["appId"]),

            ToolDef("publish_app",
                "Save a version snapshot of the app's current files. Call this after all files have been uploaded (write_file or zip upload). Creates an entry in the app's version history.",
                Props(
                    Prop("appId",   "string", "App ID — read from automaton.json"),
                    Prop("summary", "string", "One-sentence description of what changed in this version (optional)")),
                ["appId"])
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
                "create_app"     => await ExecCreateApp(args, userId),
                "write_file" or "read_file" or
                "list_files"  or "delete_file" or "delete_folder" or
                "rename_file" or "rename_folder"
                                 => await ExecFileTool(name, args),
                "list_runners"   => await ExecListRunners(userId),
                "set_app_runner" => await ExecSetAppRunner(args, userId),
                "deploy_backend" => await ExecDeployBackend(args, userId, ct),
                "publish_app"    => await ExecPublishApp(args, userId),
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
              "name": "{{name}}",
              "server": "{{server}}"
            }
            The file must contain exactly one JSON object. Delete any previous content.

            {{runnerSection}}

            RUNNER SELECTION INSTRUCTIONS:
            - First, analyse the user's request to determine whether the app will need a Python backend (e.g. database access, API calls, form submissions, server-side logic).
            - If the app is purely static UI (no backend controllers needed), skip runner selection and proceed with building.
            - If a backend is needed AND runners are listed above, ask the user: "Which runner should be used to deploy the backend? Available: <runner names>". Then call set_app_runner with the chosen runner before writing any code.
            - If a backend is needed but no runners are available, inform the user that a runner must be registered before the backend can be deployed, then continue building the app code anyway.
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

    private async Task<string> ExecSetAppRunner(JsonElement args, Guid userId)
    {
        var appId      = RequireAppId(args);
        var runnerName = RequireString(args, "runnerName");

        var runner = await _db.Runners
            .Where(r => r.RunnerUsers.Any(ru => ru.UserId == userId) && r.Name == runnerName)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException($"Runner '{runnerName}' not found or not accessible.");

        await _appsService.UpdateRunner(appId, runner.Id);
        return $"Runner '{runnerName}' assigned to app {appId}.";
    }

    private async Task<string> ExecDeployBackend(JsonElement args, Guid userId, CancellationToken ct)
    {
        var appId  = RequireAppId(args);
        var result = await _deployService.DeployBackendToRunnersAsync(appId, userId, null, ct);

        if (!result.Success && result.RunnerCount == 0)
            return "No runner is assigned to this app. Call list_runners to see available runners, " +
                   "ask the user which one to use, then call set_app_runner before retrying deploy_backend.";

        return result.Message
            ?? (result.Success
                ? $"Deployed {result.DeployedCount} script(s) to {result.RunnerCount} runner(s)"
                : "Deploy failed");
    }

    private async Task<string> ExecPublishApp(JsonElement args, Guid userId)
    {
        var appId   = RequireAppId(args);
        var summary = args.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString()
            : null;

        var version = await _appVersionService.CreateSnapshotAsync(
            appId, userId, AppVersionTrigger.ManualSave, summary: summary);

        return $"Version {version.VersionNumber} saved for app {appId}." +
               (summary != null ? $" Summary: {summary}" : string.Empty);
    }

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
