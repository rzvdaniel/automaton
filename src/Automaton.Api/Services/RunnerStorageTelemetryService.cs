using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Core.Enums;
using Automaton.Core.Models;
using Automaton.Api.Data;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Constants;
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

    public async Task ReportHostedRunnerAsync(RunnerDetails authenticatedRunner, HostedRunnerStorageTelemetry report, CancellationToken cancellationToken)
    {
        ValidateHostedRunnerReport(authenticatedRunner, report);

        var hostedRunner = await db.HostedRunners.SingleOrDefaultAsync(
            x => x.Id == report.HostedRunnerId && x.RunnerId == authenticatedRunner.Id, cancellationToken)
            ?? throw new UnauthorizedAccessException("The hosted Runner is not linked to the authenticated Runner.");
        EnsureAllowance(hostedRunner);
        var previousSelfReportedAt = hostedRunner.StorageSelfReportedAtUtc ??
            (hostedRunner.StorageMeasurementSource == StorageMeasurementSources.RunnerSelfReported
                ? hostedRunner.StorageMeasuredAtUtc : null);
        if (previousSelfReportedAt >= report.MeasuredAtUtc) return;

        var calculatedUser = checked(report.WorkspaceBytes + report.LogsBytes + report.HomeBytes);
        var calculatedTotal = checked(report.SystemBytes + calculatedUser);
        var now = DateTime.UtcNow;
        db.HostedRunnerStorageObservations.Add(CreateHostedRunnerObservation(hostedRunner, report, calculatedUser, calculatedTotal, now));
        var previousSelfTotal = hostedRunner.StorageSelfReportedTotalBytes;
        UpdateSelfReportedState(hostedRunner, report, calculatedUser, calculatedTotal);

        var authoritativeIsRecent = hostedRunner.StorageVerifiedAtUtc >= now.AddMinutes(-options.AuthoritativeValidityMinutes);
        var materiallyConflicts = HasMaterialDiscrepancy(report.Success, calculatedTotal, hostedRunner.StorageVerifiedTotalBytes);
        var suspiciousDrop = HasSuspiciousDrop(report.Success, calculatedTotal, previousSelfTotal);
        if (materiallyConflicts || suspiciousDrop)
            await RequestVerificationAsync(hostedRunner, materiallyConflicts ? "AuthoritativeDiscrepancy" : "SuspiciousDrop", now, cancellationToken);
        if (authoritativeIsRecent && (!report.Success || materiallyConflicts))
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        ApplyLatestHostedRunnerMeasurement(hostedRunner, report, calculatedUser, calculatedTotal, now);
        var newState = StorageTelemetryPolicy.EvaluateHostedRunner(
            hostedRunner.StorageAllowanceBytes, calculatedTotal, report.Success, options);
        TrackPolicyChange(hostedRunner, newState, now);
        hostedRunner.StorageState = newState;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateHostedRunnerReport(RunnerDetails authenticatedRunner, HostedRunnerStorageTelemetry report)
    {
        if (authenticatedRunner.Purpose != RunnerPurposes.Managed ||
            authenticatedRunner.HostedRunnerId != report.HostedRunnerId)
            throw new UnauthorizedAccessException("The authenticated Runner cannot report storage for this hosted Runner.");
        ValidateTimestamp(report.MeasuredAtUtc);
        ValidateText(report.Error, 2000, nameof(report.Error));
        ValidateText(report.RunnerVersion, 80, nameof(report.RunnerVersion));
        var values = new[] { report.SystemBytes, report.WorkspaceBytes, report.LogsBytes, report.HomeBytes, report.UserStorageBytes, report.TotalBytes, report.MeasurementDurationMs };
        if (values.Any(x => x < 0)) throw new ArgumentException("Storage telemetry values cannot be negative.");
    }

    private static HostedRunnerStorageObservation CreateHostedRunnerObservation(
        HostedRunner hostedRunner,
        HostedRunnerStorageTelemetry report,
        long calculatedUser,
        long calculatedTotal,
        DateTime now)
        => new()
        {
            Id = Guid.NewGuid(), HostedRunnerId = hostedRunner.Id,
            Source = StorageMeasurementSources.RunnerSelfReported,
            MeasuredAtUtc = report.MeasuredAtUtc, ReceivedAtUtc = now,
            SystemBytes = report.Success ? report.SystemBytes : 0,
            UserBytes = report.Success ? calculatedUser : 0,
            TotalBytes = report.Success ? calculatedTotal : null,
            Success = report.Success, Error = Truncate(report.Error, 2000),
            RunnerVersion = Truncate(report.RunnerVersion, 80)
        };

    private static void UpdateSelfReportedState(
        HostedRunner hostedRunner,
        HostedRunnerStorageTelemetry report,
        long calculatedUser,
        long calculatedTotal)
    {
        hostedRunner.StorageSelfReportedAtUtc = report.MeasuredAtUtc;
        hostedRunner.StorageSelfReportedStatus = report.Success ? StorageMeasurementStatuses.Succeeded : StorageMeasurementStatuses.Failed;
        hostedRunner.StorageSelfReportedError = report.Success ? null : Truncate(report.Error ?? "Storage measurement failed.", 2000);
        if (report.Success)
        {
            hostedRunner.StorageSelfReportedSystemBytes = report.SystemBytes;
            hostedRunner.StorageSelfReportedUserBytes = calculatedUser;
            hostedRunner.StorageSelfReportedTotalBytes = calculatedTotal;
        }
    }

    private static bool HasMaterialDiscrepancy(bool succeeded, long calculatedTotal, long? verifiedTotal)
        => succeeded && verifiedTotal.HasValue &&
            Math.Abs(calculatedTotal - verifiedTotal.Value) >
            Math.Max(64L * 1024 * 1024, verifiedTotal.Value / 10);

    private static bool HasSuspiciousDrop(bool succeeded, long calculatedTotal, long? previousTotal)
        => succeeded && previousTotal.HasValue && calculatedTotal < previousTotal.Value &&
           previousTotal.Value - calculatedTotal > Math.Max(256L * 1024 * 1024, previousTotal.Value / 2);

    private static void ApplyLatestHostedRunnerMeasurement(
        HostedRunner hostedRunner,
        HostedRunnerStorageTelemetry report,
        long calculatedUser,
        long calculatedTotal,
        DateTime now)
    {
        hostedRunner.StorageMeasuredAtUtc = report.MeasuredAtUtc;
        hostedRunner.StorageReceivedAtUtc = now;
        hostedRunner.StorageMeasurementSource = StorageMeasurementSources.RunnerSelfReported;
        hostedRunner.StorageMeasurementStatus = report.Success ? StorageMeasurementStatuses.Succeeded : StorageMeasurementStatuses.Failed;
        hostedRunner.StorageMeasurementError = report.Success ? null : Truncate(report.Error ?? "Storage measurement failed.", 2000);
        if (report.Success)
        {
            hostedRunner.StorageSystemBytes = report.SystemBytes;
            hostedRunner.StorageUserBytes = calculatedUser;
            hostedRunner.StorageTotalBytes = calculatedTotal;
            hostedRunner.StorageUsagePercent = hostedRunner.StorageAllowanceBytes > 0
                ? calculatedTotal * 100m / hostedRunner.StorageAllowanceBytes : null;
        }
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
            report.Success ? report.DockerDataRootTotalBytes!.Value : 0,
            report.Success ? report.DockerDataRootAvailableBytes!.Value : 0,
            report.Success, options);
        TrackRunnerHostPolicyChange(host, newState, now);
        host.StorageState = newState;
        await db.SaveChangesAsync(cancellationToken);
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
        host.StorageMeasurementStatus = report.Success ? StorageMeasurementStatuses.Succeeded : StorageMeasurementStatuses.Failed;
        host.StorageMeasurementError = report.Success ? null : Truncate(report.Error ?? "Host storage measurement failed.", 2000);
        if (report.Success)
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

    internal static void EnsureAllowance(HostedRunner hostedRunner)
    {
        if (hostedRunner.StorageAllowanceBytes <= 0 && hostedRunner.SpecificationSnapshot.Resources.DiskLimitMb > 0)
            hostedRunner.StorageAllowanceBytes = hostedRunner.SpecificationSnapshot.Resources.DiskLimitMb * 1024L * 1024L;
    }

    private async Task RequestVerificationAsync(
        HostedRunner runner,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var window = now.Ticks / TimeSpan.FromHours(1).Ticks;
        var key = $"storage-verification:{runner.Id:N}:{window}";
        if (await db.HostedRunnerStorageVerifications.AnyAsync(x => x.IdempotencyKey == key, cancellationToken))
            return;
        var verification = new HostedRunnerStorageVerification
        {
            Id = Guid.NewGuid(), HostedRunnerId = runner.Id, IdempotencyKey = key,
            Status = StorageVerificationStatuses.Pending, Reason = reason, RequestedAtUtc = now
        };
        db.HostedRunnerStorageVerifications.Add(verification);
        runner.StorageVerificationRequestedAtUtc = now;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(), Category = LogCategories.Audit, Action = AuditActions.Created,
            EntityType = AuditEntityTypes.HostedRunner, EntityId = runner.Id,
            TargetUserId = runner.UserId, Outcome = AuditOutcomes.Success,
            EventType = AuditEventTypes.HostedRunnerStorageVerificationRequested,
            Message = "Storage telemetry triggered authoritative hosted Runner verification.",
            MetadataJson = JsonSerializer.Serialize(new { verification.Id, Reason = reason }),
            CreatedAtUtc = now
        });
    }

    private void TrackPolicyChange(HostedRunner runner, string newState, DateTime now)
    {
        if (string.Equals(runner.StorageState, newState, StringComparison.Ordinal)) return;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(), Category = LogCategories.Audit, Action = AuditActions.Changed,
            EntityType = AuditEntityTypes.HostedRunner, EntityId = runner.Id,
            TargetUserId = runner.UserId, Outcome = AuditOutcomes.Success,
            EventType = StorageTelemetryAuditEvents.HostedRunnerPolicyChanged,
            Message = $"Hosted Runner storage policy changed from '{runner.StorageState}' to '{newState}'.",
            MetadataJson = JsonSerializer.Serialize(new { PreviousState = runner.StorageState, NewState = newState }),
            CreatedAtUtc = now
        });
    }
}
