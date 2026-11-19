using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Installs a default app (Server Console, VictoriaLogs) from the official catalog for one system administrator (private to them), approves the
/// permissions it asks for at that install (the installer and a person's one-button install both approve everything pending right after installing; later updates never are; each approval is recorded in the audit log) and deploys it. Used for a new
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

    /// <summary>What Server Keeper asks for: it reads server state, sends email and reads the cleanup. Informational; installs approve what the app asks.</summary>
    internal static readonly string[] ApprovedPermissions = ["server:read", "notify:email", "server:cleanup"];

    /// <summary>
    /// The apps a new server gets on its first Runner: only apps that work with no setup, each with the permissions
    /// it is expected to ask for. Add an app here when it ships and needs no setup; existing servers are never changed by this list.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> DefaultApps = new Dictionary<string, string[]>
    {
        [AppId] = ApprovedPermissions,
        ["victorialogs"] = ["logs:receive"]
    };

    private const string InstallerReason = "Approved by the installer when it installed this default app";

    private const string Reason = "Default app provisioning for the system administrator";

    /// <summary>The user's own Server Console app (installed from the official catalog entry), if any.</summary>
    public async Task<Guid?> FindAsync(Guid userId, CancellationToken token)
    {
        var entry = await EntryAsync(userId, AppId, token);
        if (entry is null) return null;
        return await FindInstalledAsync(userId, entry, token);
    }

    /// <summary>
    /// Installs when missing, approves the permissions the new app asks for and deploys it. An app that is already installed keeps its
    /// pending requests: only the install approves, as for the first-Runner installer. Impersonate or run as <paramref name="userId"/>.
    /// </summary>
    public async Task<Guid> ProvisionAsync(Guid userId, CatalogEntryDto? entry, CancellationToken token)
    {
        entry ??= await EntryAsync(userId, AppId, token) ?? throw new InvalidOperationException("Server Console is not available in the official catalog.");
        return await RunOneButtonAsync(
            permissions,
            find: () => FindInstalledAsync(userId, entry, token),
            install: async () => (await installer.InstallAsync(new CatalogInstallRequest { Repo = entry.Repo, Folder = entry.Folder, Commit = entry.Commit }, token)).Id,
            needsDeploy: appId => NeedsDeployAsync(db, appId, token),
            deploy: appId => deployer.AcceptAsync(appId, userId, userId, null, false, token),
            logger, token, entry.Name);
    }

    /// <summary>The one-button install: approves everything the app asks right after installing it, and nothing for an app that was already there.</summary>
    internal static async Task<Guid> RunOneButtonAsync(
        AppPermissionService permissions, Func<Task<Guid?>> find, Func<Task<Guid>> install,
        Func<Guid, Task<bool>> needsDeploy, Func<Guid, Task> deploy, ILogger logger, CancellationToken token, string appName)
    {
        var installedNow = false;
        return await RunAsync(
            find,
            async () => { var id = await install(); installedNow = true; return id; },
            appId => installedNow ? ApprovePendingAsync(permissions, appId, null, token) : Task.CompletedTask,
            needsDeploy, deploy, logger, token, appName);
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
    /// of it exists; the marker (the new app's id) is written right after the install, before anything can fail, together with the
    /// permissions the app asked for then (<paramref name="markInstalled"/> gets them; a retry passes them back as <paramref name="approvedAtInstall"/>
    /// and approves only those, so what an update asks for in the retry window stays pending). Once marked, a missing app was
    /// removed by the administrator and is left alone; an app other than the marked one was installed by the administrator and is never approved here.
    /// Returns null when the app was left alone.
    /// </summary>
    public async Task<Guid?> ProvisionByInstallerAsync(
        Guid userId, CatalogEntryDto entry, Guid? installedAppId, IReadOnlyCollection<string> approvedAtInstall,
        Func<Guid, IReadOnlyCollection<string>, Task> markInstalled, CancellationToken token)
        => await RunRecordedInstallerAsync(
            permissions,
            find: () => FindInstalledAsync(userId, entry, token),
            install: async () => (await installer.InstallAsync(new CatalogInstallRequest { Repo = entry.Repo, Folder = entry.Folder, Commit = entry.Commit }, token)).Id,
            installedAppId, approvedAtInstall, markInstalled,
            needsDeploy: appId => NeedsDeployAsync(db, appId, token),
            deploy: appId => deployer.AcceptAsync(appId, userId, userId, null, false, token),
            logger, token, entry.Name);

    /// <summary>The installer pass with its record: a new install notes what the app has pending and approves exactly that; a retry approves only the noted set.</summary>
    internal static async Task<Guid?> RunRecordedInstallerAsync(
        AppPermissionService permissions, Func<Task<Guid?>> find, Func<Task<Guid>> install, Guid? installedAppId,
        IReadOnlyCollection<string> approvedAtInstall, Func<Guid, IReadOnlyCollection<string>, Task> markInstalled,
        Func<Guid, Task<bool>> needsDeploy, Func<Guid, Task> deploy, ILogger logger, CancellationToken token, string appName)
    {
        var allowed = approvedAtInstall;
        return await RunInstallerAsync(
            find, install,
            markInstalled: async appId =>
            {
                allowed = (await permissions.GetStatusAsync(appId, token)).Where(s => s.Status == AppPermissionStatuses.Pending).Select(s => s.Permission).ToArray();
                await markInstalled(appId, allowed);
            },
            installedAppId,
            approve: appId => ApprovePendingAsync(permissions, appId, allowed, token, InstallerReason),
            needsDeploy, deploy, logger, token, appName);
    }

    internal static async Task<Guid?> RunInstallerAsync(
        Func<Task<Guid?>> find, Func<Task<Guid>> install, Func<Guid, Task> markInstalled, Guid? installedAppId,
        Func<Guid, Task> approve, Func<Guid, Task<bool>> needsDeploy, Func<Guid, Task> deploy,
        ILogger logger, CancellationToken token, string appName)
    {
        var existing = await find();
        // Marked: only that very app is ours (a removed one, or one the administrator installed again, is left alone).
        if (installedAppId is { } marked ? existing != marked : existing is not null) return null;
        var appId = existing ?? await install();
        var installedNow = existing is null;
        if (installedAppId is null) await markInstalled(appId);
        // Approval belongs to the first install: this pass, or a retry of one that never got its first deploy. Permissions an
        // update asks for later (the app was deployed by then) stay pending, as for any app.
        var firstInstall = installedNow || await needsDeploy(appId);
        return await RunAsync(() => Task.FromResult<Guid?>(appId), install, firstInstall ? approve : _ => Task.CompletedTask, needsDeploy, deploy, logger, token, appName);
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

    /// <summary>Approves the pending permissions in <paramref name="allowed"/>, or every pending one when it is null.</summary>
    internal static async Task ApprovePendingAsync(AppPermissionService permissions, Guid appId, IReadOnlyCollection<string>? allowed, CancellationToken token, string reason = Reason)
    {
        var statuses = await permissions.GetStatusAsync(appId, token);
        foreach (var status in statuses.Where(s => s.Status == AppPermissionStatuses.Pending && (allowed is null || allowed.Contains(s.Permission))))
            await permissions.SetApprovalAsync(appId, status.Permission, true, token, reason);
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
