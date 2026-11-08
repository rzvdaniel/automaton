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
        if (!await db.Runners.AnyAsync(r => r.LastConnectedAtUtc != null, cancellationToken)) return false;
        var adminId = await db.UserRoles
            .Where(membership => db.Roles.Any(role => role.Id == membership.RoleId && role.Name == UserRole.SystemAdministrator))
            .Select(membership => membership.UserId)
            .FirstOrDefaultAsync(cancellationToken);
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

    private async Task<bool> InstallMissingAsync(IServiceProvider services, ApplicationDbContext db, Guid adminId, CancellationToken cancellationToken)
    {
        var catalog = services.GetRequiredService<AppCatalogService>();
        var response = await catalog.GetAsync(adminId, false, cancellationToken,
            new CatalogQuery { Source = CatalogSourceStore.OfficialId, PageSize = CatalogQuery.MaximumPageSize });
        if (response.Unavailable || response.Stale) return false;
        var installer = services.GetRequiredService<AppCatalogInstallService>();
        var userContext = services.GetRequiredService<UserContextService>();
        userContext.ImpersonateUser(adminId);
        try
        {
            foreach (var id in AppIds)
            {
                var entry = response.Apps.SingleOrDefault(app => app.Id == id && app.SourceId == CatalogSourceStore.OfficialId);
                if (entry is null || entry.RequiresNewerServer) return false;
                if (await db.WebApps.AnyAsync(app => app.InstalledFromRepo == entry.Repo
                    && app.InstalledFromFolder == entry.Folder && app.WebAppUsers.Any(user => user.UserId == adminId), cancellationToken)) continue;
                await installer.InstallAsync(new CatalogInstallRequest
                {
                    Repo = entry.Repo, Folder = entry.Folder, Commit = entry.Commit
                }, cancellationToken);
                logger.LogInformation("Installed essential catalog app {AppName} for the first administrator.", entry.Name);
            }
            return true;
        }
        finally { userContext.ClearImpersonation(); }
    }
}
