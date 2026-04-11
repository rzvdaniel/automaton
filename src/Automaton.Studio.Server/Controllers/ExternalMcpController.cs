using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Automaton.Studio.Server.Services.Mcp.Apps;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
///   write_file      — Write a file into the app
///   read_file       — Read a file from the app
///   list_files      — List all app files
///   delete_file     — Delete one or more files
///   deploy_backend  — Deploy backend Python scripts to the configured runner
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
    private readonly AppFileStorageService _fileStorage;
    private readonly UserContextService _userContext;
    private readonly AppsControllerExecutionService _deployService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ExternalMcpController> _logger;

    public ExternalMcpController(
        AppsService appsService,
        AppFileStorageService fileStorage,
        UserContextService userContext,
        AppsControllerExecutionService deployService,
        IWebHostEnvironment env,
        ILogger<ExternalMcpController> logger)
    {
        _appsService = appsService;
        _fileStorage = fileStorage;
        _userContext = userContext;
        _deployService = deployService;
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
                "Write a file to the Automaton app. Creates parent directories if needed. Overwrites if the file already exists.",
                Props(
                    Prop("appId",   "string", "App ID — read from automaton.json"),
                    Prop("path",    "string", "Relative file path, e.g. 'index.html' or 'backend/score.py'"),
                    Prop("content", "string", "Full file content")),
                ["appId", "path", "content"]),

            ToolDef("read_file",
                "Read the content of a file from the Automaton app.",
                Props(
                    Prop("appId", "string", "App ID — read from automaton.json"),
                    Prop("path",  "string", "Relative file path")),
                ["appId", "path"]),

            ToolDef("list_files",
                "List all files currently in the Automaton app.",
                Props(
                    Prop("appId", "string", "App ID — read from automaton.json")),
                ["appId"]),

            ToolDef("delete_file",
                "Delete one or more files from the Automaton app.",
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

            ToolDef("deploy_backend",
                "Deploy Python backend scripts to the app's configured runner. Call this after writing backend/*.py files.",
                Props(
                    Prop("appId", "string", "App ID — read from automaton.json")),
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
                "write_file"     => await ExecWriteFile(args, userId),
                "read_file"      => await ExecReadFile(args, userId),
                "list_files"     => await ExecListFiles(args, userId),
                "delete_file"    => await ExecDeleteFile(args, userId),
                "deploy_backend" => await ExecDeployBackend(args, userId, ct),
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

        // Return the automaton.json content so the AI writes it to the local workspace.
        return $$"""
            App '{{name}}' created with ID: {{appId}}

            Write the following to 'automaton.json' in the project root using your local file tools:
            {
              "appId": "{{appId}}",
              "name": "{{name}}",
              "server": "{{server}}"
            }
            """;
    }

    private async Task<string> ExecWriteFile(JsonElement args, Guid userId)
    {
        var appId   = RequireAppId(args);
        var path    = RequireString(args, "path");
        var content = RequireString(args, "content");
        var fs      = GetFileSystem(userId, appId);
        await fs.WriteFileAsync(path, content);
        return $"Written: {path} ({content.Split('\n').Length} lines)";
    }

    private async Task<string> ExecReadFile(JsonElement args, Guid userId)
    {
        var appId   = RequireAppId(args);
        var path    = RequireString(args, "path");
        var fs      = GetFileSystem(userId, appId);
        var content = await fs.ReadFileAsync(path);
        if (content == null)
            throw new FileNotFoundException($"File not found: {path}. Call list_files to see available files.");
        return content;
    }

    private async Task<string> ExecListFiles(JsonElement args, Guid userId)
    {
        var appId = RequireAppId(args);
        var fs    = GetFileSystem(userId, appId);
        var files = (await fs.ListFilesAsync()).ToList();
        return files.Count == 0 ? "(no files yet)" : string.Join('\n', files);
    }

    private async Task<string> ExecDeleteFile(JsonElement args, Guid userId)
    {
        var appId = RequireAppId(args);
        if (!args.TryGetProperty("paths", out var pathsProp) || pathsProp.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Missing required argument 'paths' (array)");

        var fs      = GetFileSystem(userId, appId);
        var deleted = new List<string>();
        foreach (var el in pathsProp.EnumerateArray())
        {
            var path = el.GetString();
            if (!string.IsNullOrWhiteSpace(path))
            {
                await fs.DeleteFileAsync(path);
                deleted.Add(path);
            }
        }
        return $"Deleted {deleted.Count} file(s): {string.Join(", ", deleted)}";
    }

    private async Task<string> ExecDeployBackend(JsonElement args, Guid userId, CancellationToken ct)
    {
        var appId  = RequireAppId(args);
        var result = await _deployService.DeployBackendToRunnersAsync(appId, userId, null, ct);
        return result.Message
            ?? (result.Success
                ? $"Deployed {result.DeployedCount} script(s) to {result.RunnerCount} runner(s)"
                : "Deploy failed");
    }

    private AppScopedFileSystem GetFileSystem(Guid userId, Guid appId)
    {
        if (!_fileStorage.AppExists(userId, appId))
            throw new InvalidOperationException($"App {appId} not found or not accessible.");
        var appPath = _fileStorage.GetAppDirectoryPath(userId, appId);
        return new AppScopedFileSystem(appPath);
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
