using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Automaton.Api.Controllers;

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
///         "headers": { "X-API-Key": "ak_workflow_xxx" }
///       }
///     }
///   }
///
/// ## Available Tools (read / discovery only — deploy via automaton CLI)
///   create_app      — Create a new app; returns appId to store in automaton.json
///   list_user_apps  — List the current user's accessible apps
///   read_file       — Read a file (supports line ranges)
///   list_files      — List editable app source files
///   get_file_tree   — Return the editable source file tree for an app
///   list_runners    — List the user's connected runners
///
/// For file writes and deployment use the automaton CLI:
///   automaton deploy   — build, upload, and deploy the application
///   automaton pull     — sync server files locally, with overwrite confirmation for matching local files
///   automaton publish  — save a version snapshot
///   automaton runners  — list runners
///
/// ## Auth
///   X-API-Key: ak_workflow_xxx  (header)
///   ?api_key=ak_workflow_xxx    (query parameter)
///   Authorization: Bearer ak_workflow_xxx  (Bearer with ak_ prefix)
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

    private readonly ExternalMcpToolExecutionService _toolExecutionService;
    private readonly UserContextService _userContext;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ExternalMcpController> _logger;

    public ExternalMcpController(
        ExternalMcpToolExecutionService toolExecutionService,
        UserContextService userContext,
        IWebHostEnvironment env,
        ILogger<ExternalMcpController> logger)
    {
        _toolExecutionService = toolExecutionService;
        _userContext = userContext;
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
    public async Task<IActionResult> Handle(
        [FromBody] JsonElement body,
        [FromHeader(Name = "X-API-Key")] string[]? apiKeyHeaders,
        [FromHeader(Name = "Authorization")] string[]? authorizationHeaders,
        CancellationToken ct)
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
                var r = await ProcessOneRequest(item, userId, apiKeyHeaders, authorizationHeaders, ct);
                if (r != null) results.Add(r);
            }
            return Ok(results);
        }

        // Single request
        var response = await ProcessOneRequest(body, userId, apiKeyHeaders, authorizationHeaders, ct);
        if (response == null) return NoContent(); // notification — no response required
        return Ok(response);
    }

    // ── JSON-RPC dispatcher ───────────────────────────────────────────────────

    private async Task<object?> ProcessOneRequest(
        JsonElement req,
        Guid userId,
        string[]? apiKeyHeaders,
        string[]? authorizationHeaders,
        CancellationToken ct)
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
                "initialize" => HandleInitialize(req, apiKeyHeaders?.FirstOrDefault(), authorizationHeaders?.FirstOrDefault()),
                "tools/list" => ExternalMcpToolCatalog.List(),
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

    private object HandleInitialize(JsonElement req, string? apiKeyHeader, string? authorizationHeader)
    {
        var protocolVersion = "2024-11-05";
        if (req.TryGetProperty("params", out var p) && p.TryGetProperty("protocolVersion", out var pv))
            protocolVersion = pv.GetString() ?? protocolVersion;

        var serverUrl = $"{Request.Scheme}://{Request.Host}";
        var apiKey    = apiKeyHeader ?? ExtractBearerApiKey(authorizationHeader);

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

    private async Task<object> HandleToolCall(JsonElement req, Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!req.TryGetProperty("params", out var p))
            throw new ArgumentException("Missing 'params' in tools/call request");

        var name = p.GetProperty("name").GetString()
            ?? throw new ArgumentException("Missing tool name");
        var args = p.TryGetProperty("arguments", out var a) ? a : default;

        try
        {
            var serverUrl = $"{Request.Scheme}://{Request.Host}";
            var text = await _toolExecutionService.ExecuteAsync(name, args, userId, serverUrl);
            return ToolResult(text);
        }
        catch (Exception ex)
        {
            return ToolError(ex.Message);
        }
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

}
