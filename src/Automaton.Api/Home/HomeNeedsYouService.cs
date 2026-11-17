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
                waiting.Count == 1 ? $"/apps/{waiting.Keys.First()}?view=admin" : "/admin/apps", false));
        if (!await SmtpConfiguredAsync(token))
            cards.Add(new(HomeCardKeys.NoSmtp, HomeCardKeys.NoSmtp, "info", "Set up email",
                "Invitations and password recovery need SMTP settings.", "Open settings", "/settings#server", true));
        if (!BackupRecent(await InstallationBackups.ReadAsync(configuration, token)))
            cards.Add(new(HomeCardKeys.NoBackup, HomeCardKeys.NoBackup, "info", "Back up this server",
                "No recent successful backup was found.", "Open backups", "/admin/backups", true));
        cards.AddRange(await RecommendedAppCardsAsync(userId, token));
        return cards;
    }

    /// <summary>One optional card per recommended official-catalog app the administrator has not installed.</summary>
    private async Task<List<HomeCard>> RecommendedAppCardsAsync(Guid userId, CancellationToken token)
    {
        try
        {
            var response = await catalog.GetAsync(userId, false, token,
                new CatalogQuery { Source = CatalogSourceStore.OfficialId, PageSize = CatalogQuery.MaximumPageSize });
            if (response.Unavailable) return [];
            return RecommendedCards(response.Apps);
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
                app.ActionsOnly ? "Its last Action run failed." : app.LiveEnvironment?.LastDeploymentError ?? "It is not running.",
                "Open app", $"/apps/{app.Id}", false, app.Id);
        }
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
        return new(HomeCardKeys.ServerUpdate, HomeCardKeys.ServerUpdate, "warn", "Update the server",
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
