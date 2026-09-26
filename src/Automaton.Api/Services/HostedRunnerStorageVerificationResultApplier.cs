using System.Text.Json;
using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Enums;
using Automaton.Api.Logging;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>Applies authoritative storage measurements reported by Infrastructure Runners.</summary>
internal static class HostedRunnerStorageVerificationResultApplier
{
    public static async Task ApplyAsync(
        ApplicationDbContext db,
        HostedRunner hostedRunner,
        HostedRunnerDeployment deployment,
        JsonElement result,
        StorageTelemetryOptions options,
        CancellationToken cancellationToken)
    {
        RunnerStorageTelemetryService.EnsureAllowance(hostedRunner);
        var verification = await db.HostedRunnerStorageVerifications
            .FirstOrDefaultAsync(item => item.Id == deployment.Id, cancellationToken)
            ?? throw new InvalidOperationException("Storage verification request was not found.");
        var systemBytes = ReadNonNegativeInt64(result, "systemBytes");
        var userBytes = ReadNonNegativeInt64(result, "userBytes");
        var totalBytes = ReadNonNegativeInt64(result, "totalBytes");
        var filesystemUuid = ReadFilesystemUuid(result);
        EnsureAuthoritativeStorageResult(result, systemBytes, userBytes, totalBytes);
        var allocation = await db.AccountStorageAllocations
            .SingleOrDefaultAsync(item => item.AccountId == hostedRunner.UserId, cancellationToken)
            ?? throw new InvalidOperationException("Account storage allocation was not found.");
        if (!hostedRunner.RunnerHostId.HasValue
            || allocation.StorageRunnerHostId != hostedRunner.RunnerHostId)
            throw new InvalidOperationException("Account storage locality does not match the Hosted Runner host.");
        if (allocation.StorageFilesystemUuid is not null
            && !string.Equals(allocation.StorageFilesystemUuid, filesystemUuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Storage verification reported a different XFS filesystem than the account storage locality.");
        allocation.StorageFilesystemUuid ??= filesystemUuid;

        var now = DateTime.UtcNow;
        db.HostedRunnerStorageObservations.Add(new HostedRunnerStorageObservation
        {
            Id = Guid.NewGuid(),
            HostedRunnerId = hostedRunner.Id,
            Source = StorageMeasurementSources.InfrastructureRunnerVerified,
            MeasuredAtUtc = now,
            ReceivedAtUtc = now,
            SystemBytes = systemBytes,
            UserBytes = userBytes,
            TotalBytes = totalBytes,
            Success = true
        });
        ApplyMeasurement(hostedRunner, systemBytes, userBytes, totalBytes, now, options, db);
        verification.Status = StorageVerificationStatuses.Succeeded;
        verification.CompletedAtUtc = now;
        verification.Error = null;
    }

    private static void EnsureAuthoritativeStorageResult(
        JsonElement result,
        long systemBytes,
        long userBytes,
        long totalBytes)
    {
        if (!result.TryGetProperty("usageSource", out var usageSource)
            || usageSource.ValueKind != JsonValueKind.String
            || !string.Equals(usageSource.GetString(), "xfs-project-quota", StringComparison.Ordinal))
            throw new InvalidOperationException("Storage verification did not report authoritative XFS project-quota usage.");
        if (systemBytes + userBytes != totalBytes)
            throw new InvalidOperationException("Storage verification returned inconsistent totals.");
    }

    private static string ReadFilesystemUuid(JsonElement result)
    {
        if (!result.TryGetProperty("storageFilesystemUuid", out var element)
            || element.ValueKind != JsonValueKind.String
            || !Guid.TryParse(element.GetString(), out var filesystemUuid))
            throw new InvalidOperationException("Storage verification returned an invalid XFS filesystem UUID.");
        return filesystemUuid.ToString("D");
    }

    private static void ApplyMeasurement(
        HostedRunner hostedRunner,
        long systemBytes,
        long userBytes,
        long totalBytes,
        DateTime now,
        StorageTelemetryOptions options,
        ApplicationDbContext db)
    {
        hostedRunner.StorageVerifiedAtUtc = now;
        hostedRunner.StorageVerifiedSystemBytes = systemBytes;
        hostedRunner.StorageVerifiedUserBytes = userBytes;
        hostedRunner.StorageVerifiedTotalBytes = totalBytes;
        hostedRunner.StorageMeasuredAtUtc = now;
        hostedRunner.StorageReceivedAtUtc = now;
        hostedRunner.StorageMeasurementSource = StorageMeasurementSources.InfrastructureRunnerVerified;
        hostedRunner.StorageMeasurementStatus = StorageMeasurementStatuses.Succeeded;
        hostedRunner.StorageSystemBytes = systemBytes;
        hostedRunner.StorageUserBytes = userBytes;
        hostedRunner.StorageTotalBytes = totalBytes;
        hostedRunner.StorageUsagePercent = hostedRunner.StorageAllowanceBytes > 0
            ? totalBytes * 100m / hostedRunner.StorageAllowanceBytes
            : null;
        var newStorageState = StorageTelemetryPolicy.EvaluateHostedRunner(
            hostedRunner.StorageAllowanceBytes, totalBytes, success: true, options);
        TrackPolicyChange(db, hostedRunner, newStorageState, now);
        hostedRunner.StorageState = newStorageState;
        hostedRunner.StorageMeasurementError = null;
    }

    private static void TrackPolicyChange(
        ApplicationDbContext db,
        HostedRunner hostedRunner,
        string newStorageState,
        DateTime now)
    {
        if (string.Equals(hostedRunner.StorageState, newStorageState, StringComparison.Ordinal))
            return;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            Category = LogCategories.Audit,
            Action = AuditActions.Changed,
            EntityType = AuditEntityTypes.HostedRunner,
            EntityId = hostedRunner.Id,
            TargetUserId = hostedRunner.UserId,
            Outcome = AuditOutcomes.Success,
            EventType = StorageTelemetryAuditEvents.HostedRunnerPolicyChanged,
            Message = $"Hosted Runner storage policy changed from '{hostedRunner.StorageState}' to '{newStorageState}'.",
            MetadataJson = JsonSerializer.Serialize(new { PreviousState = hostedRunner.StorageState, NewState = newStorageState }),
            CreatedAtUtc = now
        });
    }

    private static long ReadNonNegativeInt64(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var element) || !element.TryGetInt64(out var value) || value < 0)
            throw new InvalidOperationException($"Infrastructure Action returned invalid {property}.");
        return value;
    }
}
