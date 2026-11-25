using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Automaton.Api.Mcp;

/// <summary>Read-only tools of the Automaton MCP endpoint (<c>/mcp</c>).</summary>
[McpServerToolType]
public sealed class AppMcpTools(AppMcpReader reader)
{
    [McpServerTool(Name = "list_apps", ReadOnly = true), Description("List the Automaton apps you can see, with state and address.")]
    public Task<object> ListApps(CancellationToken token) => Run(() => reader.ListAppsAsync(token));

    [McpServerTool(Name = "app_status", ReadOnly = true), Description("State, version, Runner, address, full url and who can open one app.")]
    public Task<object> AppStatus([Description("The app id from list_apps")] Guid appId, CancellationToken token)
        => Run(() => reader.StatusAsync(appId, token));

    [McpServerTool(Name = "app_activity", ReadOnly = true), Description("Recent runs, deploys, events and failures of one app.")]
    public Task<object> AppActivity([Description("The app id from list_apps")] Guid appId, CancellationToken token)
        => Run(() => reader.ActivityAsync(appId, token));

    [McpServerTool(Name = "app_logs", ReadOnly = true), Description("The last lines of the logs of an app's latest run (or a given run), at most 500.")]
    public Task<object> AppLogs(
        [Description("The app id from list_apps")] Guid appId,
        [Description("A run id from app_activity; the latest run when empty")] Guid? runId = null,
        [Description("How many of the last lines, 1 to 500; 100 when empty")] int? lines = null,
        CancellationToken token = default)
        => Run(() => reader.LogsAsync(appId, runId, lines, token));

    [McpServerTool(Name = "app_docs", ReadOnly = true), Description("The README of one app you manage.")]
    public Task<object> AppDocs([Description("The app id from list_apps")] Guid appId, CancellationToken token)
        => Run(() => reader.DocsAsync(appId, token));

    private static async Task<object> Run(Func<Task<object>> read)
    {
        try { return await read(); }
        catch (KeyNotFoundException ex) { throw new McpException(ex.Message); }
    }
}
