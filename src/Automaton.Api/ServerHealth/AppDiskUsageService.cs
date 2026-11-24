using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.ServerHealth;

/// <summary>Disk an app uses, as measured by a Runner, and its running containers' CPU and memory. Total is the sum of the disk parts that were measured.</summary>
public sealed record AppDiskUsageResponse(
    Guid AppId, string? AppName, long? FilesBytes, long? DataBytes, long? LogsBytes, long? ContainerBytes, long? ImageBytes,
    long TotalBytes, DateTime? MeasuredAtUtc, double? CpuPercent = null, long? MemoryBytes = null,
    long? HostDiskTotalBytes = null, long? HostDiskAvailableBytes = null)
{
    public static AppDiskUsageResponse From(AppDiskUsage usage, string? name, DateTime? measuredAt, long? hostTotal = null, long? hostAvailable = null) => new(
        usage.AppId, name, usage.FilesBytes, usage.DataBytes, usage.LogsBytes, usage.ContainerBytes, usage.ImageBytes,
        (usage.FilesBytes ?? 0) + (usage.DataBytes ?? 0) + (usage.LogsBytes ?? 0) + (usage.ContainerBytes ?? 0) + (usage.ImageBytes ?? 0),
        measuredAt, usage.CpuPercent, usage.MemoryBytes, hostTotal, hostAvailable);
}

/// <summary>Reads the per-app disk figures the Machine Management Runners report with their host health.</summary>
public sealed class AppDiskUsageService(ApplicationDbContext db, ResourceAuthorizationService authorization, UserContextService user)
{
    /// <summary>The app's disk use; null while no Runner has measured it. Throws when the person may not manage the app.</summary>
    public async Task<AppDiskUsageResponse?> GetAsync(Guid appId, bool administrator, CancellationToken token)
    {
        var app = await db.WebApps.AsNoTracking().Where(item => item.Id == appId && item.DeletionClaimId == null)
            .Select(item => new { item.Name }).FirstOrDefaultAsync(token) ?? throw new KeyNotFoundException("App not found");
        if (!administrator)
        {
            var userId = user.GetUserId();
            if (!await authorization.CanAccessAppAsync(appId, userId, token)) throw new KeyNotFoundException("App not found");
            if (!await authorization.CanUpdateAppAsync(appId, userId, token))
                throw new UnauthorizedAccessException("You do not have permission to see this app's disk use.");
        }
        var reports = await db.Runners.AsNoTracking()
            .Where(item => item.ApplicationProfile == RunnerApplicationProfiles.MachineManagement && item.MachineHostHealthJson != null && item.DeletionRequestedAtUtc == null)
            .Select(item => item.MachineHostHealthJson).ToListAsync(token);
        // The app lives on one machine: take the report that measured the most of it.
        return reports.Select(HostHealthReportService.Parse).OfType<MachineHostHealth>()
            .Select(host => (Host: host, Usage: host.AppDisk?.FirstOrDefault(item => item.AppId == appId)))
            .Where(item => item.Usage is not null)
            .OrderByDescending(item => AppDiskUsageResponse.From(item.Usage!, null, null).TotalBytes)
            .Select(item => AppDiskUsageResponse.From(item.Usage!, app.Name, item.Host.MeasuredAtUtc, item.Host.DiskTotalBytes, item.Host.DiskAvailableBytes)).FirstOrDefault();
    }
}
