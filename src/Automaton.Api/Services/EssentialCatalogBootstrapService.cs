using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Installs the default apps (Server Keeper, VictoriaLogs) once, after an administrator and Runner exist; an app removed later is never installed again. The other apps are only recommended on Home.</summary>
public sealed class EssentialCatalogBootstrapService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<EssentialCatalogBootstrapService> logger) : BackgroundService
{
    internal const string CompletionKey = "Apps.Catalog.EssentialsInstalled";
    internal static readonly string[] AppIds = [.. ServerConsoleProvisioningService.DefaultApps.Keys];
    internal const string StartedKey = "Apps.Catalog.EssentialsStartedAt";
    internal const string InstalledKeyPrefix = "Apps.Catalog.EssentialInstalled.";
    /// <summary>The permissions an app asked for at install (comma-separated); a retry approves only these.</summary>
    internal const string ApprovedKeyPrefix = "Apps.Catalog.EssentialApproved.";
    internal static readonly TimeSpan RetryWindow = TimeSpan.FromHours(24);
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

            var started = await StartedAtAsync(db, cancellationToken);
            if (!await InstallMissingAsync(scope.ServiceProvider, db, adminId, cancellationToken))
            {
                if (!WindowElapsed(started, DateTime.UtcNow)) return false;
                logger.LogWarning("Essential catalog installation gave up after {Hours} hours; default apps not installed by now stay uninstalled.", RetryWindow.TotalHours);
            }
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

    internal static string[] ParseApproved(string? value)
        => string.IsNullOrEmpty(value) ? [] : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static bool WindowElapsed(DateTime startedUtc, DateTime nowUtc) => nowUtc - startedUtc >= RetryWindow;

    private static async Task<DateTime> StartedAtAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == StartedKey, cancellationToken);
        if (setting is not null && DateTime.TryParse(setting.Value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var started)) return started;
        var now = DateTime.UtcNow;
        db.Settings.Add(new Setting { Key = StartedKey, Value = now.ToString("O") });
        await db.SaveChangesAsync(cancellationToken);
        return now;
    }

    /// <summary>
    /// The default apps this catalog can install: an app the official catalog lacks, or that needs a newer server, is skipped
    /// and does not hold back the others. Null when the catalog itself is unavailable or stale.
    /// </summary>
    internal static (IReadOnlyList<CatalogEntryDto> Available, IReadOnlyList<string> Skipped)? SelectEssentials(CatalogResponse response)
    {
        if (response.Unavailable || response.Stale) return null;
        var available = new List<CatalogEntryDto>();
        var skipped = new List<string>();
        foreach (var id in AppIds)
        {
            var entry = response.Apps.SingleOrDefault(app => app.Id == id && app.SourceId == CatalogSourceStore.OfficialId);
            if (entry is null || entry.RequiresNewerServer) skipped.Add(id);
            else available.Add(entry);
        }
        return (available, skipped);
    }

    private async Task<bool> InstallMissingAsync(IServiceProvider services, ApplicationDbContext db, Guid adminId, CancellationToken cancellationToken)
    {
        var catalog = services.GetRequiredService<AppCatalogService>();
        var response = await catalog.GetAsync(adminId, false, cancellationToken,
            new CatalogQuery { Source = CatalogSourceStore.OfficialId, PageSize = CatalogQuery.MaximumPageSize });
        if (SelectEssentials(response) is not var (available, skipped)) return false;
        foreach (var id in skipped)
            logger.LogInformation("Default app {AppId} is missing from the official catalog or needs a newer server; skipped for now.", id);
        var userContext = services.GetRequiredService<UserContextService>();
        userContext.ImpersonateUser(adminId);
        try
        {
            // Each app is marked as installed by the installer right after its install; only a marked app is resumed, never reinstalled.
            foreach (var entry in available)
            {
                var key = InstalledKeyPrefix + entry.Id;
                var marker = await db.Settings.Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
                Guid? installedAppId = marker is null ? null : Guid.TryParse(marker, out var parsed) ? parsed : Guid.Empty;
                var approved = await db.Settings.Where(s => s.Key == ApprovedKeyPrefix + entry.Id).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
                // No record (an install from before it was kept) approves nothing on a retry.
                var appId = await services.GetRequiredService<ServerConsoleProvisioningService>().ProvisionByInstallerAsync(adminId, entry, installedAppId, ParseApproved(approved), async (newAppId, asked) =>
                {
                    db.Settings.Add(new Setting { Key = key, Value = newAppId.ToString() });
                    db.Settings.Add(new Setting { Key = ApprovedKeyPrefix + entry.Id, Value = string.Join(',', asked) });
                    await db.SaveChangesAsync(cancellationToken);
                }, cancellationToken);
                if (appId is not null) logger.LogInformation("Installed or completed default catalog app {AppName} for the first administrator.", entry.Name);
            }
            return skipped.Count == 0;
        }
        finally { userContext.ClearImpersonation(); }
    }
}
