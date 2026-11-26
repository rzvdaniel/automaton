using Automaton.Core.Models;

namespace Automaton.Api.ServerHealth;

/// <param name="Reported">False until a Machine Management Runner has reported, or when it could not run the check.</param>
/// <param name="NeedsYou">True when there is at least one finding; Server Keeper shows it as a "needs you" item.</param>
public sealed record HostSecurityResponse(
    DateTime GeneratedAtUtc, bool Reported, bool NeedsYou, int? ReportAgeSeconds, string? Firewall, IReadOnlyList<HostSecurityFinding> Findings);

/// <summary>The SSH and firewall findings the Machine Management Runner last read from the host, for apps with <c>security:read</c>.</summary>
public sealed class HostSecurityReadService(HostHealthReportService reports)
{
    public async Task<HostSecurityResponse> GetAsync(DateTime nowUtc, CancellationToken token)
    {
        var host = await reports.LatestAsync(token);
        if (host?.HostSecurity is not { } status) return new(nowUtc, false, false, null, null, []);
        return new(nowUtc, true, status.Findings.Count > 0, (int)Math.Max(0, (nowUtc - host.MeasuredAtUtc).TotalSeconds), status.Firewall, status.Findings);
    }
}
