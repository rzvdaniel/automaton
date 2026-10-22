using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class RunnerStorageTelemetryService(ApplicationDbContext db)
{
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(10);
    public async Task ReportMachineRunnerAsync(
        RunnerDetails authenticatedRunner,
        MachineRunnerStorageTelemetry report,
        CancellationToken cancellationToken)
    {
        EnsureMachineRunnerIdentity(authenticatedRunner);
        ValidateMachineRunnerReport(report);

        var runner = await db.Runners.SingleOrDefaultAsync(
            x => x.Id == authenticatedRunner.Id &&
                x.Purpose == RunnerPurposes.Managed &&
                x.ApplicationProfile == RunnerApplicationProfiles.MachineManagement &&
                x.InstallationId == authenticatedRunner.InstallationId,
            cancellationToken)
            ?? throw new UnauthorizedAccessException("The Machine Management Runner identity changed before storage was reported.");
        if (runner.MachineStorageMeasuredAtUtc >= report.MeasuredAtUtc) return;

        runner.MachineStorageMeasuredAtUtc = report.MeasuredAtUtc;
        runner.MachineStorageMeasurementStatus = report.Success ? StorageMeasurementStatuses.Succeeded : StorageMeasurementStatuses.Failed;
        runner.MachineStorageMeasurementError = report.Success ? null : Truncate(report.Error ?? "Machine storage measurement failed.", 2000);
        if (report.Success)
        {
            runner.MachineStorageTotalBytes = report.TotalBytes;
            runner.MachineStorageAvailableBytes = report.AvailableBytes;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureMachineRunnerIdentity(RunnerDetails runner)
    {
        if (runner.Purpose != RunnerPurposes.Managed ||
            runner.ApplicationProfile != RunnerApplicationProfiles.MachineManagement ||
            string.IsNullOrWhiteSpace(runner.InstallationId))
            throw new UnauthorizedAccessException("Only an enrolled Machine Management Runner can report machine storage.");
    }

    private static void ValidateMachineRunnerReport(MachineRunnerStorageTelemetry report)
    {
        ValidateTimestamp(report.MeasuredAtUtc);
        ValidateText(report.Error, 2000, nameof(report.Error));
        ValidateText(report.RunnerVersion, 80, nameof(report.RunnerVersion));
        if (report.TotalBytes < 0 || report.AvailableBytes < 0 || report.AvailableBytes > report.TotalBytes)
            throw new ArgumentException("Machine storage values must be non-negative and internally consistent.");
    }

    private static void ValidateTimestamp(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        if ((DateTime.UtcNow - utc).Duration() > MaximumClockSkew)
            throw new ArgumentException("Storage telemetry timestamp is outside the accepted clock-skew window.");
    }

    private static void ValidateText(string? value, int maximum, string name)
    {
        if (value?.Length > maximum) throw new ArgumentException($"{name} exceeds {maximum} characters.");
    }

    private static string? Truncate(string? value, int maximum) =>
        string.IsNullOrEmpty(value) ? value : value[..Math.Min(value.Length, maximum)];

}
