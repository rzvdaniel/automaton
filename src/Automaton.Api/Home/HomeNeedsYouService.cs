using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.ServerHealth;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Home;

/// <summary>Works out the Needs-you cards from existing state. A card exists only while its task is open.</summary>
public class HomeNeedsYouService(
    ApplicationDbContext db,
    IConfiguration configuration,
    SmtpSettingsService smtpSettings,
    AdminAppDirectoryService appDirectory,
    AppCatalogService catalog,
    LatestReleaseClient releases,
    IOptions<HostedRunnerOptions> runnerOptions)
{
    private static readonly string[] FailedActionStatuses = ["Failed", "TimedOut", "Lost", "RunnerUnavailable"];

    public virtual async Task<List<HomeCard>> ServerCardsAsync(Guid userId, CancellationToken token)
    {
        var cards = new List<HomeCard>();
        if (!await db.Runners.AnyAsync(runner => runner.DeletionRequestedAtUtc == null, token))
            cards.Add(new(HomeCardKeys.NoRunner, HomeCardKeys.NoRunner, "warn", "Add a Runner",
                "No Runner is connected, so apps have nowhere to run.", "Add a Runner", "/server", false));
        if (await ServerUpdateAsync(token) is { } update) cards.Add(update);
        if (await RunnerUpdatesAsync(token) is { } runnerUpdate) cards.Add(runnerUpdate);
        var waiting = await appDirectory.WaitingByAppAsync(token);
        if (waiting.Count > 0)
            cards.Add(new(HomeCardKeys.PermissionRequests, HomeCardKeys.PermissionRequests, "warn",
                waiting.Count == 1 ? "An app asks for permissions" : $"{waiting.Count} apps ask for permissions",
                "Apps wait for an administrator to approve or reject what they requested.", "Review requests",
                "/admin/permission-requests", false));
        cards.AddRange(await ReadinessCardsAsync(token));
        if (!await SmtpConfiguredAsync(token))
            cards.Add(new(HomeCardKeys.NoSmtp, HomeCardKeys.NoSmtp, "info", "Set up email",
                "Invitations and password recovery need SMTP settings.", "Open settings", "/settings#email", true));
        if (!BackupRecent(await InstallationBackups.ReadAsync(configuration, token)))
            cards.Add(new(HomeCardKeys.NoBackup, HomeCardKeys.NoBackup, "info", "Back up this server",
                "No recent successful backup was found.", "Open backups", "/admin/backups", true));
        cards.AddRange(await CatalogCardsAsync(userId, token));
        return cards;
    }

    /// <summary>A card for each installation readiness check that reports an error. Backups have their own card.</summary>
    private async Task<List<HomeCard>> ReadinessCardsAsync(CancellationToken token)
    {
        try { return ReadinessCards(await InstallationReadiness.CheckAsync(db, configuration, smtpSettings, token)); }
        catch (Exception exception) when (exception is not OperationCanceledException) { return []; }
    }

    internal static List<HomeCard> ReadinessCards(IEnumerable<InstallationCheck> checks) => checks
        .Where(check => check.Status == "error" && check.Name != "Backups")
        .Select(check => new HomeCard($"{HomeCardKeys.SetupCheckPrefix}{check.Name}", HomeCardKeys.SetupCheck, "error",
            $"Check failed: {check.Name}", check.Detail, "Open server", "/server", false)).ToList();

    /// <summary>How long after the first-Runner install the "Installed for you" note is offered.</summary>
    internal static readonly TimeSpan InstalledNoteWindow = TimeSpan.FromDays(14);

    /// <summary>The note that tells an administrator of a fresh server what was installed for them: the default apps still installed, while the install is recent.</summary>
    internal static HomeCard? InstalledForYouCard(IEnumerable<CatalogEntryDto> apps, DateTime? installedAtUtc)
    {
        if (installedAtUtc is not { } at || DateTime.UtcNow - at > InstalledNoteWindow) return null;
        var installed = apps.Where(item => ServerConsoleProvisioningService.DefaultApps.ContainsKey(item.Id)
            && item.SourceId == CatalogSourceStore.OfficialId && item.InstalledAppId is not null).ToList();
        if (installed.Count == 0) return null;
        var names = string.Join(", ", installed.Select(app => app.Name));
        return new(HomeCardKeys.InstalledForYou, HomeCardKeys.InstalledForYou, "info", $"Installed for you: {names}",
            $"{names} {(installed.Count == 1 ? "is" : "are")} ready to use. To remove one, open its Settings > Danger Zone; it is not installed again.",
            "Open", $"/apps/{installed[0].InstalledAppId}", true, installed[0].InstalledAppId);
    }

    /// <summary>
    /// Backups record only a local folder, never a destination, so "backups never left this server" is judged by the
    /// Offsite Backup app: offered while the official catalog has it and the administrator has not installed it.
    /// </summary>
    internal static HomeCard? OffsiteBackupCard(IEnumerable<CatalogEntryDto> apps)
    {
        var app = apps.FirstOrDefault(item => item.Id == OffsiteBackupAppId && item.SourceId == CatalogSourceStore.OfficialId);
        return app is null || app.InstalledAppId is not null || app.RequiresNewerServer ? null
            : new(HomeCardKeys.OffsiteBackup, HomeCardKeys.OffsiteBackup, "warn", "Your backups only live on this server",
                $"If the server is lost, so are its backups. {app.Name} copies them to another place.", $"Install {app.Name}", InstallFormPath(app), true, null, app.Id);
    }

    /// <summary>The install form the Catalog card opens for this app, so a card can skip the list.</summary>
    internal static string InstallFormPath(CatalogEntryDto app)
    {
        var query = $"repo={Uri.EscapeDataString(app.Repo)}";
        if (!string.IsNullOrEmpty(app.Folder)) query += $"&folder={Uri.EscapeDataString(app.Folder)}";
        return $"/apps/install-source?{query}&commit={Uri.EscapeDataString(app.Commit)}";
    }

    internal const string OffsiteBackupAppId = "offsite-backup";

    /// <summary>One optional card per recommended official-catalog app the administrator has not installed.</summary>
    private async Task<List<HomeCard>> CatalogCardsAsync(Guid userId, CancellationToken token)
    {
        try
        {
            var response = await catalog.GetAsync(userId, false, token,
                new CatalogQuery { Source = CatalogSourceStore.OfficialId, PageSize = CatalogQuery.MaximumPageSize });
            if (response.Unavailable) return [];
            var cards = RecommendedCards(response.Apps);
            if (OffsiteBackupCard(response.Apps) is { } offsite) cards.Insert(0, offsite);
            var installedAt = await db.Settings.AsNoTracking().Where(setting => setting.Key == EssentialCatalogBootstrapService.CompletionKey)
                .Select(setting => (DateTime?)setting.UpdatedAt).FirstOrDefaultAsync(token);
            if (InstalledForYouCard(response.Apps, installedAt) is { } note) cards.Insert(0, note);
            return cards;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }
    }

    /// <summary>Recommended official-catalog apps that are not installed yet and run on this server.</summary>
    public static List<HomeCard> RecommendedCards(IEnumerable<CatalogEntryDto> apps) => apps
        .Where(app => app.Recommended && app.SourceId == CatalogSourceStore.OfficialId && app.InstalledAppId is null && !app.RequiresNewerServer)
        .OrderBy(app => app.RecommendedOrder ?? int.MaxValue)
        .Select(app => new HomeCard($"{HomeCardKeys.RecommendedPrefix}{app.Id}", HomeCardKeys.RecommendedApp, "info", $"Install {app.Name}",
            app.Summary, "Install", "/apps/catalog", true, null, app.Id)).ToList();

    /// <summary>Failed apps and apps whose Runner is offline, for apps the person can change.</summary>
    public static IEnumerable<HomeCard> AppCards(IEnumerable<Models.AppListItemDto> apps)
    {
        foreach (var app in apps.Where(item => item.CanUpdate && !item.IsPaused))
        {
            var failed = app.ActionsOnly
                ? app.LastActionRun is { } run && FailedActionStatuses.Contains(run.Status)
                : app.LiveEnvironment?.Status is "Failed" or "RunnerOffline";
            if (!failed) continue;
            yield return new($"{HomeCardKeys.AppFailed}:{app.Id}", HomeCardKeys.AppFailed, "error", $"{app.Name} is failing",
                app.ActionsOnly ? "Its last Action run failed." : ShortReason(app.LiveEnvironment?.LastDeploymentError) ?? "It is not running.",
                "Open app", $"/apps/{app.Id}", false, app.Id);
        }
    }

    /// <summary>One short line of a deployment error: its first line and, when it has more, the last (a traceback ends with the cause).</summary>
    internal static string? ShortReason(string? error)
    {
        var lines = (error ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;
        var text = lines.Length == 1 ? lines[0] : $"{lines[0].TrimEnd(':')}: {lines[^1]}";
        return text.Length <= 200 ? text : text[..199] + "…";
    }

    private static bool BackupRecent(InstallationBackupStatus backup)
        => backup.Available && backup.LastSuccessAt is { } at && DateTimeOffset.UtcNow - at <= InstallationBackups.StaleAfter;

    private async Task<bool> SmtpConfiguredAsync(CancellationToken token)
    {
        try { return (await smtpSettings.GetSummaryAsync(token)).Configured; }
        catch (SmtpSettingsUnreadableException) { return false; }
    }

    private async Task<HomeCard?> ServerUpdateAsync(CancellationToken token)
    {
        var latest = await releases.GetLatestVersionAsync(token);
        if (InstallationUpdate.Parse(configuration["Installation:Version"]) is not { } current
            || InstallationUpdate.Parse(latest) is not { } newest || newest <= current) return null;
        return new(HomeCardKeys.ServerUpdate, HomeCardKeys.ServerUpdate, "warn", "Update Automaton",
            $"{latest} is available. This server runs {configuration["Installation:Version"]}.", "Open server", "/server", false);
    }

    private async Task<HomeCard?> RunnerUpdatesAsync(CancellationToken token)
    {
        var image = runnerOptions.Value.DockerImage;
        var runners = await db.Runners.AsNoTracking()
            .Where(runner => runner.DeletionRequestedAtUtc == null && runner.InstallationId != null && runner.InstallationId != "")
            .Select(runner => runner.ImageDigest).ToListAsync(token);
        var outdated = runners.Count(digest => !RunnerUpgradeService.IsCurrent(digest, image));
        return outdated == 0 ? null : new(HomeCardKeys.RunnerUpdate, HomeCardKeys.RunnerUpdate, "warn",
            outdated == 1 ? "A Runner can be updated" : $"{outdated} Runners can be updated",
            "A newer Runner version is available.", "Open server", "/server", false);
    }
}
