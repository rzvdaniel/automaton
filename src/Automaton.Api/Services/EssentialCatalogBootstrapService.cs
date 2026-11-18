using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Installs the starter apps for a new server after an administrator and Runner exist.</summary>
public sealed class EssentialCatalogBootstrapService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<EssentialCatalogBootstrapService> logger) : BackgroundService
{
    internal const string CompletionKey = "Apps.Catalog.EssentialsInstalled";
    internal static readonly string[] AppIds = ["server-console", "website-monitor", "heartbeat"];
    private const long LockId = 827;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Catalog:InstallEssentialsOnFirstRunner", false)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                if (await RunOnceAsync(stoppingToken)) return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Essential catalog installation will retry after a failure.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (await db.Settings.AnyAsync(s => s.Key == CompletionKey, cancellationToken)) return true;
        var adminId = await FirstAdministratorWithRunnerAsync(db, cancellationToken);
        if (adminId == Guid.Empty) return false;

        // A session lock serializes API replicas; a crash releases it and the next pass resumes missing apps.
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(cancellationToken);
        var locked = false;
        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT pg_try_advisory_lock({LockId})";
                locked = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
            }
            if (!locked) return false;
            if (await db.Settings.AnyAsync(s => s.Key == CompletionKey, cancellationToken)) return true;

            if (!await InstallMissingAsync(scope.ServiceProvider, db, adminId, cancellationToken)) return false;
            db.Settings.Add(new Setting { Key = CompletionKey, Value = "true" });
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            if (locked)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT pg_advisory_unlock({LockId})";
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
            await db.Database.CloseConnectionAsync();
        }
    }

    internal static async Task<Guid> FirstAdministratorWithRunnerAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Runners.AnyAsync(r => r.LastConnectedAtUtc != null, cancellationToken)) return Guid.Empty;
        return await db.UserRoles
            .Where(membership => db.Roles.Any(role => role.Id == membership.RoleId && role.Name == UserRole.SystemAdministrator))
            .Select(membership => membership.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    internal static IReadOnlyList<CatalogEntryDto>? MissingEssentials(
        CatalogResponse response, IReadOnlySet<(string Repo, string Folder)> installed)
    {
        if (response.Unavailable || response.Stale) return null;
        var missing = new List<CatalogEntryDto>();
        foreach (var id in AppIds)
        {
            var entry = response.Apps.SingleOrDefault(app => app.Id == id && app.SourceId == CatalogSourceStore.OfficialId);
            if (entry is null || entry.RequiresNewerServer) return null;
            if (!installed.Contains((entry.Repo, entry.Folder))) missing.Add(entry);
        }
        return missing;
    }

    private async Task<bool> InstallMissingAsync(IServiceProvider services, ApplicationDbContext db, Guid adminId, CancellationToken cancellationToken)
    {
        var catalog = services.GetRequiredService<AppCatalogService>();
        var response = await catalog.GetAsync(adminId, false, cancellationToken,
            new CatalogQuery { Source = CatalogSourceStore.OfficialId, PageSize = CatalogQuery.MaximumPageSize });
        var installed = await db.WebApps
            .Where(app => app.InstalledFromRepo != null && app.WebAppUsers.Any(user => user.UserId == adminId))
            .Select(app => new { Repo = app.InstalledFromRepo!, Folder = app.InstalledFromFolder ?? "" })
            .ToListAsync(cancellationToken);
        var missing = MissingEssentials(response, installed.Select(app => (app.Repo, app.Folder)).ToHashSet());
        if (missing is null) return false;
        // Every step is safe to repeat: a pass that stopped after an install still deploys what was never deployed.
        missing = AppIds.Select(id => response.Apps.Single(app => app.Id == id && app.SourceId == CatalogSourceStore.OfficialId)).ToList();
        var installer = services.GetRequiredService<AppCatalogInstallService>();
        var userContext = services.GetRequiredService<UserContextService>();
        userContext.ImpersonateUser(adminId);
        try
        {
            foreach (var entry in missing)
            {
                if (entry.Id == ServerConsoleProvisioningService.AppId)
                    await services.GetRequiredService<ServerConsoleProvisioningService>().ProvisionAsync(adminId, entry, cancellationToken);
                else
                    await InstallAndDeployAsync(services, db, installer, adminId, entry, cancellationToken);
                logger.LogInformation("Installed or completed essential catalog app {AppName} for the first administrator.", entry.Name);
            }
            return true;
        }
        finally { userContext.ClearImpersonation(); }
    }

    /// <summary>Installs the app when missing and starts its first deploy, so it is Running and not just listed. A failed start is logged, not fatal.</summary>
    private async Task InstallAndDeployAsync(
        IServiceProvider services, ApplicationDbContext db, AppCatalogInstallService installer, Guid adminId,
        CatalogEntryDto entry, CancellationToken token)
    {
        var deployer = services.GetRequiredService<StoredSourceDeploymentService>();
        await ServerConsoleProvisioningService.RunAsync(
            find: async () => await db.WebApps
                .Where(app => app.InstalledFromRepo == entry.Repo && (app.InstalledFromFolder ?? "") == entry.Folder
                    && app.WebAppUsers.Any(user => user.UserId == adminId))
                .Select(app => (Guid?)app.Id)
                .FirstOrDefaultAsync(token),
            install: async () => (await installer.InstallAsync(
                new CatalogInstallRequest { Repo = entry.Repo, Folder = entry.Folder, Commit = entry.Commit }, token)).Id,
            approve: _ => Task.CompletedTask,
            needsDeploy: appId => ServerConsoleProvisioningService.NeedsDeployAsync(db, appId, token),
            deploy: appId => deployer.AcceptAsync(appId, adminId, adminId, null, false, token),
            logger, token, entry.Name);
    }
}
