using Automaton.Core.Models;

namespace Automaton.Api.ServerHealth;

/// <param name="Reported">False until a Machine Management Runner has reported, or when it could not read the host.</param>
/// <param name="Running">Whether fail2ban is running on the host; null when <paramref name="Reported"/> is false.</param>
public sealed record Fail2BanResponse(
    DateTime GeneratedAtUtc, bool Reported, bool? Running, int? ReportAgeSeconds,
    int BannedLastHour, int BannedLast24Hours, IReadOnlyList<Fail2BanJail> Jails);

/// <summary>The fail2ban state the Machine Management Runner last read from the host, for apps with <c>security:read</c>.</summary>
public sealed class Fail2BanReadService(HostHealthReportService reports)
{
    public async Task<Fail2BanResponse> GetAsync(DateTime nowUtc, CancellationToken token)
    {
        var host = await reports.LatestAsync(token);
        if (host?.Fail2Ban is not { } status) return new(nowUtc, false, null, null, 0, 0, []);
        var jails = status.Jails.Select(jail => jail with
            { CurrentBans = jail.CurrentBans.Where(ban => ban.ExpiresAtUtc is null || ban.ExpiresAtUtc > nowUtc).ToArray() }).ToArray();
        return new(nowUtc, true, status.Running, (int)Math.Max(0, (nowUtc - host.MeasuredAtUtc).TotalSeconds),
            jails.Sum(jail => jail.BannedLastHour), jails.Sum(jail => jail.BannedLast24Hours), jails);
    }
}
