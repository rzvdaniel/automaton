using Automaton.Api.Config;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Automaton.Api.ServerHealth;

/// <summary>Assembles the read-only server health that apps with <c>server:read</c> receive.</summary>
public sealed class ServerHealthService(
    ApplicationDbContext db,
    IConfiguration configuration,
    ConfigurationService appConfiguration,
    IOptions<HostedRunnerOptions> runnerOptions,
    HostHealthReportService reports,
    LatestReleaseClient releases,
    CertificateExpiryProbe certificates)
{
    public async Task<ServerHealthResponse> GetAsync(DateTime nowUtc, CancellationToken token)
    {
        var host = await reports.LatestAsync(token);
        host = host is null ? null : host with { AppDisk = null, Fail2Ban = null, HostSecurity = null };
        var backup = await InstallationBackups.ReadAsync(configuration, token);
        return new(nowUtc,
            await VersionAsync(token),
            host,
            host is null ? null : (int)Math.Max(0, (nowUtc - host.MeasuredAtUtc).TotalSeconds),
            await RunnersAsync(nowUtc, token),
            await CertificatesAsync(nowUtc, token),
            new(backup.Available, backup.LastAttemptAt, backup.LastSuccessAt,
                backup.LastSuccessAt is { } success ? Math.Round(Math.Max(0, (nowUtc - success.UtcDateTime).TotalHours), 1) : null,
                backup.LastError));
    }

    private async Task<ServerVersionInfo> VersionAsync(CancellationToken token)
    {
        var installed = configuration["Installation:Version"];
        var latest = await releases.GetLatestVersionAsync(token);
        var available = InstallationUpdate.Parse(installed) is { } current && InstallationUpdate.Parse(latest) is { } newest
            ? newest > current : (bool?)null;
        return new(installed, latest, available);
    }

    // Only administrator-owned Machine Management Runners are listed.
    private async Task<IReadOnlyList<ServerRunnerInfo>> RunnersAsync(DateTime nowUtc, CancellationToken token)
    {
        var image = runnerOptions.Value.DockerImage;
        var runners = await db.Runners.AsNoTracking()
            .Where(item => item.ApplicationProfile == RunnerApplicationProfiles.MachineManagement && item.DeletionRequestedAtUtc == null)
            .OrderBy(item => item.Name)
            .Select(item => new { item.Name, item.Status, item.Version, item.LastHeartbeatAtUtc, item.ImageDigest, item.InstallationId, item.MachineHostHealthJson, item.MachineHostHealthMeasuredAtUtc })
            .ToListAsync(token);
        var reported = runners.Select(item => HostHealthReportService.Parse(item.MachineHostHealthJson)).ToArray();
        var appIds = reported.SelectMany(host => host?.AppDisk ?? []).Select(usage => usage.AppId).Distinct().ToArray();
        var names = await db.WebApps.AsNoTracking().Where(app => appIds.Contains(app.Id)).ToDictionaryAsync(app => app.Id, app => app.Name, token);
        return runners.Select((item, index) =>
        {
            var host = reported[index];
            var apps = host?.AppDisk?.Select(usage => AppDiskUsageResponse.From(usage, names.GetValueOrDefault(usage.AppId), host.MeasuredAtUtc))
                .OrderByDescending(usage => usage.TotalBytes).ToArray();
            host = host is null ? null : host with { AppDisk = null, Fail2Ban = null, HostSecurity = null };
            return new ServerRunnerInfo(item.Name, item.Status, item.Version, item.LastHeartbeatAtUtc,
                !string.IsNullOrEmpty(item.InstallationId) && !RunnerUpgradeService.IsCurrent(item.ImageDigest, image),
                host, host is null ? null : (int)Math.Max(0, (nowUtc - host.MeasuredAtUtc).TotalSeconds), apps);
        }).ToArray();
    }

    public async Task<IReadOnlyList<CertificateInfo>> CertificatesAsync(DateTime nowUtc, CancellationToken token)
    {
        var hosts = new[] { appConfiguration.WebsiteUrl, runnerOptions.Value.ServerUrl }
            .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.Host : null)
            .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var result = new List<CertificateInfo>();
        foreach (var host in hosts) result.Add(await certificates.ProbeAsync(host, nowUtc, token));
        return result;
    }
}
