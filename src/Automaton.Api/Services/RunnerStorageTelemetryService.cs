using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Api.Data;
using Automaton.Api.Constants;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Logging;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Automaton.Api.Services;

public sealed class RunnerStorageTelemetryService
{
    private static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(10);
    private readonly ApplicationDbContext db;
    private readonly StorageTelemetryOptions options;

    public RunnerStorageTelemetryService(ApplicationDbContext db)
        : this(db, Options.Create(new StorageTelemetryOptions())) { }

    public RunnerStorageTelemetryService(ApplicationDbContext db, IOptions<StorageTelemetryOptions> options)
    {
        this.db = db;
        this.options = options.Value;
    }

    public async Task ReportRunnerHostAsync(RunnerDetails authenticatedRunner, RunnerHostStorageTelemetry report, CancellationToken cancellationToken)
    {
        ValidateRunnerHostReport(authenticatedRunner, report);

        var host = await db.RunnerHosts.SingleOrDefaultAsync(
            x => x.Id == report.RunnerHostId && x.InfrastructureRunnerId == authenticatedRunner.Id, cancellationToken)
            ?? throw new UnauthorizedAccessException("The Runner Host is not linked to the authenticated Infrastructure Runner.");
        if (host.StorageMeasuredAtUtc >= report.MeasuredAtUtc) return;

        var now = DateTime.UtcNow;
        UpdateRunnerHostMeasurement(host, report, now);
        var newState = StorageTelemetryPolicy.EvaluateHost(
            IsValidImageStoreMeasurement(report) ? report.DockerDataRootTotalBytes!.Value : 0,
            IsValidImageStoreMeasurement(report) ? report.DockerDataRootAvailableBytes!.Value : 0,
            IsValidImageStoreMeasurement(report), options);
        TrackRunnerHostPolicyChange(host, newState, now);
        host.StorageState = newState;
        await db.SaveChangesAsync(cancellationToken);
    }

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

    private static void ValidateRunnerHostReport(RunnerDetails authenticatedRunner, RunnerHostStorageTelemetry report)
    {
        if (authenticatedRunner.Purpose != RunnerPurposes.Infrastructure ||
            authenticatedRunner.RunnerHostId != report.RunnerHostId)
            throw new UnauthorizedAccessException("The authenticated Runner cannot report storage for this Runner Host.");
        ValidateTimestamp(report.MeasuredAtUtc);
        ValidateText(report.Error, 2000, nameof(report.Error));
        ValidateText(report.InfrastructureRunnerVersion, 80, nameof(report.InfrastructureRunnerVersion));
        ValidateText(report.FilesystemPath, 500, nameof(report.FilesystemPath));
        ValidateText(report.DockerDataRoot, 500, nameof(report.DockerDataRoot));
        if (report.TotalBytes < 0 || report.UsedBytes < 0 || report.AvailableBytes < 0)
            throw new ArgumentException("Host storage telemetry values cannot be negative.");
        if (report.DockerDataRootTotalBytes < 0 || report.DockerDataRootAvailableBytes < 0)
            throw new ArgumentException("Docker storage telemetry values cannot be negative.");
        if (report.Success && (report.UsedBytes > report.TotalBytes || report.AvailableBytes > report.TotalBytes))
            throw new ArgumentException("Host storage telemetry values are internally inconsistent.");
        if (report.Success && (!report.DockerDataRootTotalBytes.HasValue || !report.DockerDataRootAvailableBytes.HasValue ||
            report.DockerDataRootAvailableBytes > report.DockerDataRootTotalBytes))
            throw new ArgumentException("Successful host telemetry must include internally consistent Docker data-root capacity.");
    }

    private void UpdateRunnerHostMeasurement(RunnerHost host, RunnerHostStorageTelemetry report, DateTime now)
    {
        host.StorageMeasuredAtUtc = report.MeasuredAtUtc;
        host.StorageReceivedAtUtc = now;
        var imageStoreMeasurementSucceeded = IsValidImageStoreMeasurement(report);
        host.StorageMeasurementStatus = imageStoreMeasurementSucceeded ? StorageMeasurementStatuses.Succeeded : StorageMeasurementStatuses.Failed;
        host.StorageMeasurementError = imageStoreMeasurementSucceeded
            ? null
            : Truncate(GetImageStoreMeasurementError(report), 2000);
        if (imageStoreMeasurementSucceeded)
        {
            var dockerTotal = report.DockerDataRootTotalBytes!.Value;
            var dockerAvailable = report.DockerDataRootAvailableBytes!.Value;
            host.RootFilesystemTotalBytes = report.TotalBytes;
            host.RootFilesystemAvailableBytes = report.AvailableBytes;
            host.DockerFilesystemTotalBytes = dockerTotal;
            host.DockerFilesystemAvailableBytes = dockerAvailable;
            host.StorageTotalBytes = dockerTotal;
            host.StorageUsedBytes = dockerTotal - dockerAvailable;
            host.StorageAvailableBytes = dockerAvailable;
            host.StorageReserveRequiredBytes = StorageTelemetryPolicy.RequiredHostReserve(dockerTotal, options);
        }
    }

    private static bool IsValidImageStoreMeasurement(RunnerHostStorageTelemetry report)
        => report.Success && report.ImageStoreFilesystemBounded;

    private static string GetImageStoreMeasurementError(RunnerHostStorageTelemetry report)
        => !report.Success
            ? report.Error ?? "Host storage measurement failed."
            : "The Infrastructure Runner did not verify Docker image/cache storage on the bounded XFS filesystem.";

    private void TrackRunnerHostPolicyChange(RunnerHost host, string newState, DateTime now)
    {
        if (!string.Equals(host.StorageState, newState, StringComparison.Ordinal))
        {
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid(), Category = LogCategories.Audit, Action = AuditActions.Changed,
                EntityType = nameof(RunnerHost), EntityId = host.Id, Outcome = AuditOutcomes.Success,
                EventType = StorageTelemetryAuditEvents.RunnerHostPolicyChanged,
                Message = $"Runner Host storage policy changed from '{host.StorageState}' to '{newState}'.",
                MetadataJson = JsonSerializer.Serialize(new { PreviousState = host.StorageState, NewState = newState }),
                CreatedAtUtc = now
            });
        }
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
