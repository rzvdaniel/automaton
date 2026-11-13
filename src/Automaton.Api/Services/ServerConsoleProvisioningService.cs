using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Installs Server Console from the official catalog for one system administrator (private to them), approves the
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

    /// <summary>The only permissions approved at install. The app reads server state, sends email and reads the cleanup.</summary>
    internal static readonly string[] ApprovedPermissions = ["server:read", "notify:email", "server:cleanup"];

    private const string Reason = "Server Console provisioning for the system administrator";

    /// <summary>The user's own Server Console app (installed from the official catalog entry), if any.</summary>
    public async Task<Guid?> FindAsync(Guid userId, CancellationToken token)
    {
        var entry = await EntryAsync(userId, token);
        if (entry is null) return null;
        return await FindInstalledAsync(userId, entry, token);
    }

    /// <summary>Installs when missing, approves still-pending permissions and deploys a newly installed app. Impersonate or run as <paramref name="userId"/>.</summary>
    public async Task<Guid> ProvisionAsync(Guid userId, CatalogEntryDto? entry, CancellationToken token)
    {
        entry ??= await EntryAsync(userId, token) ?? throw new InvalidOperationException("Server Console is not available in the official catalog.");
        var appId = await FindInstalledAsync(userId, entry, token);
        var installedNow = appId is null;
        if (installedNow)
        {
            var app = await installer.InstallAsync(new CatalogInstallRequest { Repo = entry.Repo, Folder = entry.Folder, Commit = entry.Commit }, token);
            appId = app.Id;
        }

        // Only Pending is approved: a permission an administrator rejected or revoked stays that way.
        await ApprovePendingAsync(permissions, appId!.Value, token);

        if (installedNow)
        {
            try { await deployer.AcceptAsync(appId.Value, userId, userId, null, false, token); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Installed and approved is useful on its own; the administrator can deploy from the app page.
                logger.LogWarning(exception, "Server Console was installed but its first deploy could not be started.");
            }
        }
        return appId.Value;
    }

    internal static async Task ApprovePendingAsync(AppPermissionService permissions, Guid appId, CancellationToken token)
    {
        var statuses = await permissions.GetStatusAsync(appId, token);
        foreach (var status in statuses.Where(s => s.Status == AppPermissionStatuses.Pending && ApprovedPermissions.Contains(s.Permission)))
            await permissions.SetApprovalAsync(appId, status.Permission, true, token, Reason);
    }

    private async Task<CatalogEntryDto?> EntryAsync(Guid userId, CancellationToken token)
    {
        var response = await catalog.GetAsync(userId, false, token,
            new CatalogQuery { Source = CatalogSourceStore.OfficialId, PageSize = CatalogQuery.MaximumPageSize });
        if (response.Unavailable || response.Stale) return null;
        var entry = response.Apps.SingleOrDefault(app => app.Id == AppId && app.SourceId == CatalogSourceStore.OfficialId);
        return entry is null || entry.RequiresNewerServer ? null : entry;
    }

    private async Task<Guid?> FindInstalledAsync(Guid userId, CatalogEntryDto entry, CancellationToken token)
        => await db.WebApps
            .Where(app => app.InstalledFromRepo == entry.Repo && (app.InstalledFromFolder ?? "") == entry.Folder
                && app.WebAppUsers.Any(user => user.UserId == userId))
            .Select(app => (Guid?)app.Id)
            .FirstOrDefaultAsync(token);
}
