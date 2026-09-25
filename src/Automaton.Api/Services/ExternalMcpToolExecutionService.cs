using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Mcp.Tools;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Automaton.Api.Services;

/// <summary>
/// Executes the app and Runner operations exposed by the external MCP endpoint.
/// </summary>
public sealed class ExternalMcpToolExecutionService(
    AppsService appsService,
    AppFileStorageService fileStorageService,
    FileIoTool fileIoTool,
    UserContextService userContext,
    ApplicationDbContext db)
{
    public Task<string> ExecuteAsync(
        string toolName,
        JsonElement arguments,
        Guid userId,
        string serverUrl)
    {
        return toolName switch
        {
            "create_app" => CreateAppAsync(arguments, userId, serverUrl),
            "list_user_apps" => ListUserAppsAsync(),
            "get_file_tree" => GetFileTreeAsync(arguments),
            "read_file" or "list_files" => ExecuteFileToolAsync(toolName, arguments),
            "list_runners" => ListRunnersAsync(userId),
            _ => throw new InvalidOperationException($"Unknown tool: {toolName}")
        };
    }

    private async Task<string> CreateAppAsync(JsonElement arguments, Guid userId, string serverUrl)
    {
        var name = RequireString(arguments, "name");
        var appId = await appsService.Create(new NewApp { Name = name });
        var thresholdUtc = DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold;

        var runners = await db.Runners
            .Where(runner => runner.RunnerUsers.Any(runnerUser => runnerUser.UserId == userId))
            .OrderBy(runner => runner.Name)
            .Select(runner => new
            {
                runner.Name,
                Connected = runner.ConnectionId != null
                    && runner.LastHeartbeatAtUtc.HasValue
                    && runner.LastHeartbeatAtUtc.Value >= thresholdUtc
            })
            .ToListAsync();

        var runnerSection = runners.Count == 0
            ? "No runners are registered on this account. Register a Runner before deploying an application that requires one."
            : "Available runners:\n" + string.Join("\n", runners.Select(runner =>
                $"- {runner.Name} ({(runner.Connected ? "connected" : "offline")})"));

        return $$"""
            App '{{name}}' created with ID: {{appId}}

            IMPORTANT: Overwrite (replace entirely) 'automaton.json' in the project root with ONLY this content — do not append to any existing content:
            {
              "appId": "{{appId}}",
              "name": "{{name}}",
              "server": "{{serverUrl}}"
            }
            The file must contain exactly one JSON object. Delete any previous content.

            {{runnerSection}}

            NEXT STEPS:
            - If the application requires a Runner, assign one before deploying.
            - Run 'automaton set-runner <runnerName>' from the CLI to assign a runner.
            - Run 'automaton deploy' to upload files and deploy the application.
            """;
    }

    private async Task<string> ExecuteFileToolAsync(string toolName, JsonElement arguments)
    {
        var result = await fileIoTool.ExecuteToolAsync(toolName, arguments, null);
        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "Tool execution failed");
        return result.Content ?? string.Empty;
    }

    private Task<string> ListUserAppsAsync()
    {
        var apps = appsService.ListForMcp()
            .OrderByDescending(app => app.Updated)
            .Select(app => new
            {
                appId = app.Id,
                name = app.Name,
                summary = app.Summary,
                isPublished = app.IsPublished,
                publicUrl = app.PublicUrl,
                updatedAt = app.Updated,
                createdAt = app.Created,
                sourceAvailable = app.SourceAvailable,
                runtimeSource = app.RuntimeSource
            })
            .ToList();

        return Task.FromResult(JsonSerializer.Serialize(apps, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private Task<string> GetFileTreeAsync(JsonElement arguments)
    {
        var appId = RequireAppId(arguments);
        var rootPath = GetAppSourceRootPath(appId);

        if (!Directory.Exists(rootPath))
            return Task.FromResult("{\n  \"type\": \"folder\",\n  \"name\": \".\",\n  \"path\": \"\",\n  \"children\": []\n}");

        var tree = BuildFileTree(rootPath, rootPath, ".");
        return Task.FromResult(JsonSerializer.Serialize(tree, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private async Task<string> ListRunnersAsync(Guid userId)
    {
        var thresholdUtc = DateTime.UtcNow - RunnerPresenceEvaluator.OfflineThreshold;
        var runners = await db.Runners
            .Where(runner => runner.RunnerUsers.Any(runnerUser => runnerUser.UserId == userId))
            .OrderBy(runner => runner.Name)
            .Select(runner => new
            {
                runner.Name,
                Connected = runner.ConnectionId != null
                    && runner.LastHeartbeatAtUtc.HasValue
                    && runner.LastHeartbeatAtUtc.Value >= thresholdUtc
            })
            .ToListAsync();

        if (runners.Count == 0)
            return "No runners found. Register a runner first via the Automaton Studio Runners page.";

        var lines = runners.Select(runner => $"- {runner.Name} ({(runner.Connected ? "connected" : "offline")})");
        return "Available runners:\n" + string.Join("\n", lines);
    }

    private string GetAppSourceRootPath(Guid appId)
    {
        var app = appsService.Get(appId)
            ?? throw new InvalidOperationException($"App {appId} not found or not accessible.");

        var ownerId = app.UserId ?? userContext.GetUserId();
        return fileStorageService.GetAppSourceDirectoryPath(ownerId, appId);
    }

    private static Guid RequireAppId(JsonElement arguments)
    {
        var raw = RequireString(arguments, "appId");
        if (!Guid.TryParse(raw, out var appId))
            throw new ArgumentException($"'appId' is not a valid GUID: {raw}");
        return appId;
    }

    private static string RequireString(JsonElement arguments, string key)
    {
        if (!arguments.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"Missing required argument '{key}'");
        return value.GetString()!;
    }

    private static object BuildFileTree(string rootPath, string currentPath, string name)
    {
        var relativePath = Path.GetRelativePath(rootPath, currentPath).Replace('\\', '/');
        if (relativePath == ".")
            relativePath = string.Empty;

        var directories = Directory.GetDirectories(currentPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => BuildFileTree(rootPath, path, Path.GetFileName(path)));

        var files = Directory.GetFiles(currentPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new
            {
                type = "file",
                name = Path.GetFileName(path),
                path = Path.GetRelativePath(rootPath, path).Replace('\\', '/')
            });

        return new
        {
            type = "folder",
            name,
            path = relativePath,
            children = directories.Concat<object>(files).ToArray()
        };
    }
}
