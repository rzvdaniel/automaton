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
        var backup = await InstallationBackups.ReadAsync(configuration, token);
        return new(nowUtc,
            await VersionAsync(token),
            host,
            host is null ? null : (int)Math.Max(0, (nowUtc - host.MeasuredAtUtc).TotalSeconds),
            await RunnersAsync(token),
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
    private async Task<IReadOnlyList<ServerRunnerInfo>> RunnersAsync(CancellationToken token)
    {
        var image = runnerOptions.Value.DockerImage;
        var runners = await db.Runners.AsNoTracking()
            .Where(item => item.ApplicationProfile == RunnerApplicationProfiles.MachineManagement && item.DeletionRequestedAtUtc == null)
            .OrderBy(item => item.Name)
            .Select(item => new { item.Name, item.Status, item.Version, item.LastHeartbeatAtUtc, item.ImageDigest, item.InstallationId })
            .ToListAsync(token);
        return runners.Select(item => new ServerRunnerInfo(item.Name, item.Status, item.Version, item.LastHeartbeatAtUtc,
            !string.IsNullOrEmpty(item.InstallationId) && !RunnerUpgradeService.IsCurrent(item.ImageDigest, image))).ToArray();
    }

    private async Task<IReadOnlyList<CertificateInfo>> CertificatesAsync(DateTime nowUtc, CancellationToken token)
    {
        var hosts = new[] { appConfiguration.WebsiteUrl, runnerOptions.Value.ServerUrl }
            .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.Host : null)
            .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var result = new List<CertificateInfo>();
        foreach (var host in hosts) result.Add(await certificates.ProbeAsync(host, nowUtc, token));
        return result;
    }
}
