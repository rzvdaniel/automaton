using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Home;
using Automaton.Api.ServerHealth;
using Automaton.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Notifications;

/// <summary>
/// The one notification feed behind Studio Home, the CLI and the MCP tool. Administrators also get server notices;
/// everyone gets notices about the apps they can change. Acknowledgements belong to one person.
/// </summary>
public class NotificationService(
    ApplicationDbContext db,
    ResourceAuthorizationService authorization,
    HomeNeedsYouService needsYou,
    ServerHealthService serverHealth,
    IConfiguration configuration)
{
    public const int CertificateWarningDays = 14;
    public const int CertificateCriticalDays = 3;

    public async Task<IReadOnlyList<NotificationItem>> ListAsync(Guid userId, CancellationToken token)
    {
        var items = await CurrentAsync(userId, token);
        var acknowledged = await db.UserNotificationAcks.AsNoTracking().Where(ack => ack.UserId == userId).ToListAsync(token);
        return items.Where(entry => !entry.Item.Acknowledgeable || !acknowledged.Any(ack => ack.Key == entry.Item.Key && ack.Fingerprint == entry.Fingerprint))
            .Select(entry => entry.Item).OrderBy(entry => Rank(entry.Severity)).ThenBy(entry => entry.Key, StringComparer.Ordinal).ToList();
    }

    public enum AcknowledgeResult { Done, NotFound, Critical }

    public async Task<AcknowledgeResult> AcknowledgeAsync(Guid userId, string key, CancellationToken token)
    {
        var found = (await CurrentAsync(userId, token)).FirstOrDefault(entry => entry.Item.Key == key);
        if (found.Item is null) return AcknowledgeResult.NotFound;
        if (!found.Item.Acknowledgeable) return AcknowledgeResult.Critical;
        var ack = await db.UserNotificationAcks.SingleOrDefaultAsync(item => item.UserId == userId && item.Key == key, token);
        if (ack is null) db.UserNotificationAcks.Add(ack = new UserNotificationAck { UserId = userId, Key = key });
        ack.Fingerprint = found.Fingerprint;
        ack.AcknowledgedAtUtc = DateTime.UtcNow;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException)
        {
            // A parallel request acknowledged it first; anything else is a real failure.
            db.ChangeTracker.Clear();
            if (!await db.UserNotificationAcks.AsNoTracking().AnyAsync(item => item.UserId == userId && item.Key == key && item.Fingerprint == found.Fingerprint, token)) throw;
        }
        return AcknowledgeResult.Done;
    }

    private static int Rank(string severity) => severity switch { NotificationSeverity.Critical => 0, NotificationSeverity.Warning => 1, _ => 2 };

    private async Task<List<(NotificationItem Item, string Fingerprint)>> CurrentAsync(Guid userId, CancellationToken token)
    {
        var result = new List<(NotificationItem, string)>();
        if (await authorization.IsSystemAdministratorAsync(userId, token))
        {
            foreach (var card in await needsYou.ServerCardsAsync(token))
            {
                if (card.Kind == HomeCardKeys.ServerUpdate)
                    result.Add(Make(NotificationKinds.ServerUpdate, NotificationSeverity.Info, card.Title, card.Detail, card.ActionLabel, card.ActionPath, [card.Detail]));
                else if (card.Kind == HomeCardKeys.PermissionRequests)
                    result.Add(Make(NotificationKinds.PermissionRequests, NotificationSeverity.Warning, card.Title, card.Detail, card.ActionLabel, card.ActionPath, [card.Title, card.ActionPath]));
            }
            result.AddRange(await RunnersOfflineAsync(token));
            result.AddRange(await CertificatesAsync(token));
            if (await BackupFailedAsync(token) is { } backup) result.Add(backup);
        }
        var apps = await new AppListingService(db, authorization).ListAsync(null, token);
        var failing = HomeNeedsYouService.AppCards(apps).ToList();
        if (failing.Count > 0)
            result.Add(Make(NotificationKinds.AppFailed, NotificationSeverity.Warning,
                failing.Count == 1 ? failing[0].Title : $"{failing.Count} apps are failing",
                failing.Count == 1 ? failing[0].Detail : string.Join(", ", failing.Select(card => card.Title.Replace(" is failing", ""))),
                "Open app" + (failing.Count == 1 ? "" : "s"), failing.Count == 1 ? failing[0].ActionPath : "/apps",
                failing.Select(card => card.AppId?.ToString() ?? card.Key).ToList(), failing.Count));
        return result;
    }

    private async Task<IEnumerable<(NotificationItem, string)>> RunnersOfflineAsync(CancellationToken token)
    {
        var runners = await db.Runners.AsNoTracking().Where(runner => runner.DeletionRequestedAtUtc == null && runner.LastHeartbeatAtUtc != null)
            .Select(runner => new { runner.Id, runner.Name, runner.ConnectionId, runner.LastHeartbeatAtUtc }).ToListAsync(token);
        var now = DateTime.UtcNow;
        var offline = runners.Where(runner => !RunnerPresenceEvaluator.IsOnline(runner.ConnectionId, runner.LastHeartbeatAtUtc, now))
            .OrderBy(runner => runner.Name, StringComparer.Ordinal).ToList();
        if (offline.Count == 0) return [];
        var none = offline.Count == runners.Count;
        return [Make(NotificationKinds.RunnerOffline, none ? NotificationSeverity.Critical : NotificationSeverity.Warning,
            offline.Count == 1 ? $"Runner {offline[0].Name} is offline" : $"{offline.Count} Runners are offline",
            none ? "No Runner is connected, so apps cannot run or deploy." : "Apps on it cannot run or deploy until it reconnects.",
            "Open server", "/server", offline.Select(runner => runner.Id.ToString()).ToList(), offline.Count, offline.Select(runner => runner.Name).ToList())];
    }

    protected virtual Task<IReadOnlyList<CertificateInfo>> ReadCertificatesAsync(CancellationToken token)
        => serverHealth.CertificatesAsync(DateTime.UtcNow, token);

    private async Task<IEnumerable<(NotificationItem, string)>> CertificatesAsync(CancellationToken token)
    {
        var expiring = (await ReadCertificatesAsync(token))
            .Where(cert => cert.DaysRemaining is { } days && days <= CertificateWarningDays).OrderBy(cert => cert.Host, StringComparer.Ordinal).ToList();
        if (expiring.Count == 0) return [];
        var critical = expiring.Any(cert => cert.DaysRemaining <= CertificateCriticalDays);
        var soonest = expiring.Min(cert => cert.DaysRemaining!.Value);
        return [Make(NotificationKinds.CertificateExpiring, critical ? NotificationSeverity.Critical : NotificationSeverity.Warning,
            expiring.Count == 1 ? $"The certificate for {expiring[0].Host} expires soon" : $"{expiring.Count} certificates expire soon",
            soonest < 0 ? "A certificate has expired." : $"The first expires in {soonest} day{(soonest == 1 ? "" : "s")}. Renew it before visitors see warnings.",
            "Open server", "/server", expiring.Select(cert => cert.Host).ToList(), expiring.Count, expiring.Select(cert => cert.DaysRemaining < 0 ? $"{cert.Host} (expired)" : $"{cert.Host} ({cert.DaysRemaining} days)").ToList())];
    }

    private async Task<(NotificationItem, string)?> BackupFailedAsync(CancellationToken token)
    {
        var backup = await InstallationBackups.ReadAsync(configuration, token);
        if (!backup.Available || string.IsNullOrWhiteSpace(backup.LastError)) return null;
        return Make(NotificationKinds.BackupFailed, NotificationSeverity.Warning, "The last backup failed", backup.LastError!,
            "Open backups", "/admin/backups", [backup.LastAttemptAt?.UtcTicks.ToString() ?? "failed"]);
    }

    private static (NotificationItem, string) Make(string kind, string severity, string title, string detail, string label, string path,
        IReadOnlyList<string> fingerprint, int count = 1, IReadOnlyList<string>? subjects = null)
        => (new NotificationItem(kind, kind, severity, title, detail, label, path, count, subjects ?? [], severity != NotificationSeverity.Critical),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("|", fingerprint.Order(StringComparer.Ordinal))))));
}
