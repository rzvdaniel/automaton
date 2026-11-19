using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Installs a default app (Server Console, VictoriaLogs) from the official catalog for one system administrator (private to them), approves the
/// permissions it asks for (only those listed here, each recorded in the audit log) and deploys it. Used for a new
/// server and for the one-button install on an existing server; it never runs without one of those two triggers.
/// </summary>
public sealed class ServerConsoleProvisioningService(
    ApplicationDbContext db,
    AppCatalogService catalog,
    AppCatalogInstallService installer,
    AppPermissionService permissions,
    StoredSourceDeploymentService deployer,
    ILogger<ServerConsoleProvisioningService> logger)
{
    public const string AppId = "server-console";

    /// <summary>The only permissions approved at install of Server Keeper. The app reads server state, sends email and reads the cleanup.</summary>
    internal static readonly string[] ApprovedPermissions = ["server:read", "notify:email", "server:cleanup"];

    /// <summary>
    /// The apps a new server gets on its first Runner: only apps that work with no setup, each with the only permissions
    /// approved for it. Add an app here when it ships and needs no setup; existing servers are never changed by this list.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> DefaultApps = new Dictionary<string, string[]>
    {
        [AppId] = ApprovedPermissions,
        ["victorialogs"] = ["logs:receive"]
    };

    private const string Reason = "Default app provisioning for the system administrator";

    /// <summary>The user's own Server Console app (installed from the official catalog entry), if any.</summary>
    public async Task<Guid?> FindAsync(Guid userId, CancellationToken token)
    {
        var entry = await EntryAsync(userId, AppId, token);
        if (entry is null) return null;
        return await FindInstalledAsync(userId, entry, token);
    }

    /// <summary>Installs when missing, approves still-pending permissions and deploys a newly installed app. Impersonate or run as <paramref name="userId"/>.</summary>
    public async Task<Guid> ProvisionAsync(Guid userId, CatalogEntryDto? entry, CancellationToken token)
    {
        entry ??= await EntryAsync(userId, AppId, token) ?? throw new InvalidOperationException("Server Console is not available in the official catalog.");
        return await RunAsync(
            find: () => FindInstalledAsync(userId, entry, token),
            install: async () => (await installer.InstallAsync(new CatalogInstallRequest { Repo = entry.Repo, Folder = entry.Folder, Commit = entry.Commit }, token)).Id,
            approve: appId => ApprovePendingAsync(permissions, appId, DefaultApps.GetValueOrDefault(entry.Id, []), token),
            needsDeploy: appId => NeedsDeployAsync(db, appId, token),
            deploy: appId => deployer.AcceptAsync(appId, userId, userId, null, false, token),
            logger, token, entry.Name);
    }

    /// <summary>
    /// The steps in order. Every step is safe to repeat, so a pass that stopped after the install resumes here: approve what is
    /// still pending and deploy an app that has never been deployed. A failed deploy is logged, not fatal.
    /// </summary>
    internal static async Task<Guid> RunAsync(
        Func<Task<Guid?>> find, Func<Task<Guid>> install, Func<Guid, Task> approve,
        Func<Guid, Task<bool>> needsDeploy, Func<Guid, Task> deploy, ILogger logger, CancellationToken token,
        string appName = "Server Console")
    {
        var appId = await find() ?? await install();
        await approve(appId);
        if (await needsDeploy(appId))
        {
            try { await deploy(appId); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Installed and approved is useful on its own; the administrator can deploy from the app page.
                logger.LogWarning(exception, "{AppName} was installed but its first deploy could not be started.", appName);
            }
        }
        return appId;
    }

    /// <summary>
    /// Provisioning by the first-Runner installer. An app is installed only while <paramref name="installedAppId"/> is null and nothing
    /// of it exists; the marker (the new app's id) is written right after the install, before anything can fail. Once marked, a missing app was
    /// removed by the administrator and is left alone; an app other than the marked one was installed by the administrator and is never approved here.
    /// Returns null when the app was left alone.
    /// </summary>
    public async Task<Guid?> ProvisionByInstallerAsync(
        Guid userId, CatalogEntryDto entry, Guid? installedAppId, Func<Guid, Task> markInstalled, CancellationToken token)
        => await RunInstallerAsync(
            find: () => FindInstalledAsync(userId, entry, token),
            install: async () => (await installer.InstallAsync(new CatalogInstallRequest { Repo = entry.Repo, Folder = entry.Folder, Commit = entry.Commit }, token)).Id,
            markInstalled, installedAppId,
            approve: appId => ApprovePendingAsync(permissions, appId, DefaultApps.GetValueOrDefault(entry.Id, []), token),
            needsDeploy: appId => NeedsDeployAsync(db, appId, token),
            deploy: appId => deployer.AcceptAsync(appId, userId, userId, null, false, token),
            logger, token, entry.Name);

    internal static async Task<Guid?> RunInstallerAsync(
        Func<Task<Guid?>> find, Func<Task<Guid>> install, Func<Guid, Task> markInstalled, Guid? installedAppId,
        Func<Guid, Task> approve, Func<Guid, Task<bool>> needsDeploy, Func<Guid, Task> deploy,
        ILogger logger, CancellationToken token, string appName)
    {
        var existing = await find();
        // Marked: only that very app is ours (a removed one, or one the administrator installed again, is left alone).
        if (installedAppId is { } marked ? existing != marked : existing is not null) return null;
        var appId = existing ?? await install();
        if (installedAppId is null) await markInstalled(appId);
        return await RunAsync(() => Task.FromResult<Guid?>(appId), install, approve, needsDeploy, deploy, logger, token, appName);
    }

    /// <summary>
    /// True until a first deploy was ever requested. Installing already creates the default deployment target (the Runner
    /// assignment), so "has no target" is never true; a deploy operation or a deployed runtime is what shows one was started.
    /// </summary>
    internal static async Task<bool> NeedsDeployAsync(ApplicationDbContext db, Guid appId, CancellationToken token)
        => !await db.DeploymentOperations.AnyAsync(operation => operation.WebAppId == appId, token)
            && !await db.AppDeploymentTargets.AnyAsync(target => target.WebAppId == appId
                && (target.GenericRuntimeDeployed || target.Status == DeploymentTargetStatus.Running || target.Status == DeploymentTargetStatus.Deploying), token);

    /// <summary>True when the app's runtime is up on a Runner, so Open shows the app and not "not deployed yet".</summary>
    public async Task<bool> IsRunningAsync(Guid appId, CancellationToken token)
        => await db.AppDeploymentTargets.AnyAsync(target => target.WebAppId == appId && target.Status == DeploymentTargetStatus.Running, token);

    internal static async Task ApprovePendingAsync(AppPermissionService permissions, Guid appId, IReadOnlyCollection<string> allowed, CancellationToken token)
    {
        var statuses = await permissions.GetStatusAsync(appId, token);
        foreach (var status in statuses.Where(s => s.Status == AppPermissionStatuses.Pending && allowed.Contains(s.Permission)))
            await permissions.SetApprovalAsync(appId, status.Permission, true, token, Reason);
    }

    private async Task<CatalogEntryDto?> EntryAsync(Guid userId, string id, CancellationToken token)
    {
        var response = await catalog.GetAsync(userId, false, token,
            new CatalogQuery { Source = CatalogSourceStore.OfficialId, PageSize = CatalogQuery.MaximumPageSize });
        if (response.Unavailable || response.Stale) return null;
        var entry = response.Apps.SingleOrDefault(app => app.Id == id && app.SourceId == CatalogSourceStore.OfficialId);
        return entry is null || entry.RequiresNewerServer ? null : entry;
    }

    private async Task<Guid?> FindInstalledAsync(Guid userId, CatalogEntryDto entry, CancellationToken token)
        => await db.WebApps
            .Where(app => app.InstalledFromRepo == entry.Repo && (app.InstalledFromFolder ?? "") == entry.Folder
                && app.WebAppUsers.Any(user => user.UserId == userId))
            .Select(app => (Guid?)app.Id)
            .FirstOrDefaultAsync(token);
}
