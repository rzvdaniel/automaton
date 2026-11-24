using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Automaton.Api.Mcp;

/// <summary>Tools of the Automaton MCP endpoint (<c>/mcp</c>) that create, deploy, share and pause apps. Deleting is not offered.</summary>
[McpServerToolType]
public sealed class AppMcpManageTools(AppMcpManager manager)
{
    [McpServerTool(Name = "create_app", Destructive = false), Description("Create an empty app. Then call deploy_app with its files.")]
    public Task<object> CreateApp([Description("The app's name")] string name, CancellationToken token = default)
        => Run(() => manager.CreateAppAsync(name, null, token));

    [McpServerTool(Name = "deploy_app", Destructive = false), Description("Deploy an app from files given in the call (path to text content, including automaton.json). Waits until the app is Running, or returns the failure with the latest log lines. Call app_guidance for the automaton.json format.")]
    public Task<object> DeployApp(
        [Description("The app id from create_app or list_apps")] Guid appId,
        [Description("Relative file path to file content, for example {\"automaton.json\": \"...\", \"app.py\": \"...\"}")] Dictionary<string, string> files,
        CancellationToken token = default)
        => Run(() => manager.DeployAppAsync(appId, files, token));

    [McpServerTool(Name = "share_app", Destructive = false, Idempotent = true), Description("Choose who can open an app: me (only you), people, team, server (everyone signed in) or internet (public; needs publicUrl, the address name).")]
    public Task<object> ShareApp(
        [Description("The app id")] Guid appId,
        [Description("me, people, team, server or internet")] string audience,
        [Description("For internet: the public address name; keeps the current one when empty")] string? publicUrl = null,
        CancellationToken token = default)
        => Run(() => manager.ShareAppAsync(appId, audience, publicUrl, token));

    [McpServerTool(Name = "pause_app", Destructive = false, Idempotent = true), Description("Pause an app without deleting it.")]
    public Task<object> PauseApp([Description("The app id")] Guid appId, CancellationToken token = default)
        => Run(() => manager.PauseAsync(appId, true, token));

    [McpServerTool(Name = "resume_app", Destructive = false, Idempotent = true), Description("Resume a paused app.")]
    public Task<object> ResumeApp([Description("The app id")] Guid appId, CancellationToken token = default)
        => Run(() => manager.PauseAsync(appId, false, token));

    [McpServerTool(Name = "missing_secrets", ReadOnly = true), Description("Names of the secrets the app declares but has no value for, with the Studio link to fill them in. Values never pass through MCP.")]
    public Task<object> MissingSecrets([Description("The app id")] Guid appId, CancellationToken token = default)
        => Run(() => manager.MissingSecretsAsync(appId, token));

    [McpServerTool(Name = "list_catalog", ReadOnly = true), Description("Search the app catalog.")]
    public Task<object> ListCatalog([Description("Words to match name, summary and tags")] string? query = null, CancellationToken token = default)
        => Run(() => manager.ListCatalogAsync(query, token));

    [McpServerTool(Name = "install_from_catalog", Destructive = false), Description("Install an app from the catalog using repo, folder and commit from list_catalog.")]
    public Task<object> InstallFromCatalog(
        [Description("Repository (owner/name)")] string repo,
        [Description("Pinned commit")] string commit,
        [Description("Folder inside the repository; empty for the root")] string? folder = null,
        [Description("A different name for the new app")] string? name = null,
        CancellationToken token = default)
        => Run(() => manager.InstallFromCatalogAsync(repo, folder, commit, name, token));

    [McpServerTool(Name = "app_guidance", ReadOnly = true), Description("The AI guidance files the CLI writes with 'automaton new': how an Automaton app is built, automaton.json and its rules.")]
    public Task<object> AppGuidance(CancellationToken token = default) => Run(() => manager.GuidanceAsync(token));

    private static async Task<object> Run(Func<Task<object>> action)
    {
        try { return await action(); }
        catch (Exception ex) when (ex is McpUserException or KeyNotFoundException) { throw new McpException(ex.Message); }
    }
}
