using System.Text.Json;
using Automaton.Api.Data;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.ServerHealth;

/// <summary>Stores and reads the host figures a Machine Management Runner reports.</summary>
public sealed class HostHealthReportService(ApplicationDbContext db)
{
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task ReportAsync(Guid apiKeyId, MachineHostHealth report, CancellationToken token)
    {
        var runner = await db.Runners.SingleOrDefaultAsync(item => item.ApiKeyId == apiKeyId, token);
        if (runner is null || runner.Purpose != RunnerPurposes.Managed
            || runner.ApplicationProfile != RunnerApplicationProfiles.MachineManagement
            || string.IsNullOrWhiteSpace(runner.InstallationId))
            throw new UnauthorizedAccessException("The API key is not assigned to a Machine Management Runner.");
        Validate(report);
        if (runner.MachineHostHealthMeasuredAtUtc >= report.MeasuredAtUtc) return;

        runner.MachineHostHealthJson = JsonSerializer.Serialize(report, Json);
        runner.MachineHostHealthMeasuredAtUtc = report.MeasuredAtUtc;
        await db.SaveChangesAsync(token);
    }

    /// <summary>The newest report of any Machine Management Runner, or null when none has reported.</summary>
    public async Task<MachineHostHealth?> LatestAsync(CancellationToken token)
    {
        var json = await db.Runners.AsNoTracking()
            .Where(item => item.ApplicationProfile == RunnerApplicationProfiles.MachineManagement && item.MachineHostHealthJson != null && item.DeletionRequestedAtUtc == null)
            .OrderByDescending(item => item.MachineHostHealthMeasuredAtUtc)
            .Select(item => item.MachineHostHealthJson).FirstOrDefaultAsync(token);
        if (json is null) return null;
        try { return JsonSerializer.Deserialize<MachineHostHealth>(json, Json); }
        catch (JsonException) { return null; }
    }

    private static void Validate(MachineHostHealth report)
    {
        var measured = report.MeasuredAtUtc.Kind == DateTimeKind.Utc ? report.MeasuredAtUtc : report.MeasuredAtUtc.ToUniversalTime();
        if ((DateTime.UtcNow - measured).Duration() > MaximumClockSkew)
            throw new ArgumentException("The host health timestamp is outside the accepted clock-skew window.");
        long?[] sizes = [report.MemoryTotalBytes, report.MemoryAvailableBytes, report.DiskTotalBytes, report.DiskAvailableBytes,
            report.DockerImagesBytes, report.DockerContainersBytes, report.DockerVolumesBytes, report.DockerBuildCacheBytes, report.UptimeSeconds];
        if (sizes.Any(value => value < 0) || report.UpdatesPending < 0 || report.SecurityUpdates < 0)
            throw new ArgumentException("Host health values must not be negative.");
        if (report.MemoryAvailableBytes > report.MemoryTotalBytes || report.DiskAvailableBytes > report.DiskTotalBytes)
            throw new ArgumentException("Available space cannot exceed the total.");
        if (Length(report.OsName) > 200 || Length(report.OsVersion) > 100 || Length(report.RunnerVersion) > 80)
            throw new ArgumentException("A host health text value is too long.");
    }

    private static int Length(string? value) => value?.Length ?? 0;
}
