using Automaton.Api.Entities;
using Automaton.Api.Constants;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;

namespace Automaton.Api.Controllers.Admin;

internal static class RunnerHostAdminResponseMapper
{
    public static RunnerHostResponse ToResponse(RunnerHost host)
    {
        var assigned = host.HostedRunners
            .Where(HostedRunnerComputeReservationPolicy.OccupiesCapacity)
            .ToList();
        var computeReservations = assigned.Select(HostedRunnerComputeReservationPolicy.Calculate).ToArray();

        return new RunnerHostResponse
        {
            Id = host.Id,
            Name = host.Name,
            Provider = host.Provider,
            Region = host.Region,
            PublicIp = host.PublicIp,
            PrivateIp = host.PrivateIp,
            ProviderResourceId = host.ProviderResourceId,
            ProviderServerType = host.ProviderServerType,
            ProviderImageId = host.ProviderImageId,
            ProvisioningTransport = host.ProvisioningTransport,
            RuntimeConnectivityMode = host.RuntimeConnectivityMode,
            HostedRunnerSchedulingEnabled = host.HostedRunnerSchedulingEnabled,
            Status = host.Status,
            ProvisioningStatus = host.ProvisioningStatus,
            ProvisioningStartedAtUtc = host.ProvisioningStartedAtUtc,
            InfrastructureRunnerEnrolledAtUtc = host.InfrastructureRunnerEnrolledAtUtc,
            ValidatedAtUtc = host.ValidatedAtUtc,
            ApprovedAtUtc = host.ApprovedAtUtc,
            ApprovedByUserId = host.ApprovedByUserId,
            ProvisioningFailureCode = host.ProvisioningFailureCode,
            ProvisioningFailureMessage = host.ProvisioningFailureMessage,
            IsSchedulingEligible = host.IsSchedulingEligible,
            TotalCpuCores = host.TotalCpuCores,
            ReservedCpuCores = host.ReservedCpuCores,
            UsedCpuCores = computeReservations.Sum(x => x.CpuCores),
            TotalMemoryMb = host.TotalMemoryMb,
            ReservedMemoryMb = host.ReservedMemoryMb,
            UsedMemoryMb = computeReservations.Sum(x => x.MemoryMb),
            TotalDiskMb = host.TotalDiskMb,
            ReservedDiskMb = host.ReservedDiskMb,
            UsedDiskMb = assigned.Sum(x => x.SpecificationSnapshot.Resources.DiskLimitMb),
            StorageMeasuredAtUtc = host.StorageMeasuredAtUtc,
            StorageAvailableBytes = host.StorageAvailableBytes,
            StorageReserveRequiredBytes = host.StorageReserveRequiredBytes,
            StorageState = host.StorageState,
            StorageMeasurementStatus = host.StorageMeasurementStatus,
            StorageMeasurementError = host.StorageMeasurementError,
            HostedRunnerCount = assigned.Count,
            CreatedAt = host.CreatedAt,
            UpdatedAt = host.UpdatedAt,
            InfrastructureRunnerId = host.InfrastructureRunnerId,
            InfrastructureRunnerName = host.InfrastructureRunner?.Name,
            InfrastructureRunnerPurpose = host.InfrastructureRunner?.Purpose,
            InfrastructureRunnerStatus = host.InfrastructureRunner == null
                ? RunnerStatus.None.ToString()
                : RunnerPresenceEvaluator.DeriveStatus(
                    host.InfrastructureRunner.ConnectionId,
                    host.InfrastructureRunner.LastHeartbeatAtUtc,
                    DateTime.UtcNow).ToString(),
            InfrastructureRunnerLastHeartbeatAtUtc = host.InfrastructureRunner?.LastHeartbeatAtUtc
            ,InfrastructureRunnerOnboardingStatus = GetInfrastructureRunnerOnboardingStatus(host)
            ,InfrastructureRunnerVersion = host.InfrastructureRunner?.Version
            ,InfrastructureRunnerImageDigest = host.InfrastructureRunner?.ImageDigest
            ,InfrastructureRunnerSpecificationVersion = host.InfrastructureRunner?.ContainerSpecificationVersion
            ,InfrastructureRunnerApiCompatibilityVersion = host.InfrastructureRunner?.ApiCompatibilityVersion
            ,InfrastructureRunnerOperatingSystem = host.InfrastructureRunner?.OperatingSystem
            ,InfrastructureRunnerArchitecture = host.InfrastructureRunner?.Architecture
            ,InfrastructureRunnerContainerHealth = host.InfrastructureRunner?.ContainerHealth
            ,InfrastructureRunnerInstallationId = host.InfrastructureRunner?.InstallationId
            ,InfrastructureRunnerCapabilities = host.InfrastructureRunner == null
                ? Array.Empty<string>()
                : RunnerCapabilities.Deserialize(host.InfrastructureRunner.CapabilitiesJson, host.InfrastructureRunner.Purpose)
            ,LastHeartbeatAtUtc = host.LastHeartbeatAtUtc
            ,MetadataJson = host.MetadataJson
            ,HealthJson = host.HealthJson
            ,InfrastructureEnrollmentExpiresAtUtc = host.RunnerEnrollments
                .Where(x => x.Status == RunnerEnrollmentStatuses.Pending)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => (DateTime?)x.ExpiresAtUtc)
                .FirstOrDefault()
        };
    }

    public static string GetEnrollmentKeyStatus(ApiKey key)
    {
        if (!key.IsActive && key.UsedAt.HasValue)
            return "Consumed";
        if (!key.IsActive)
            return "Revoked";
        if (key.ExpiresAt.HasValue && key.ExpiresAt.Value <= DateTime.UtcNow)
            return "Expired";
        return "Pending";
    }

    private static string GetInfrastructureRunnerOnboardingStatus(RunnerHost host)
    {
        if (host.InfrastructureRunner != null)
        {
            return RunnerPresenceEvaluator.IsOnline(
                host.InfrastructureRunner.ConnectionId,
                host.InfrastructureRunner.LastHeartbeatAtUtc,
                DateTime.UtcNow)
                ? "Online"
                : "Offline";
        }

        var latestEnrollment = host.RunnerEnrollments
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        return latestEnrollment?.Status switch
        {
            RunnerEnrollmentStatuses.Pending => "EnrollmentPending",
            RunnerEnrollmentStatuses.Revoked => "Revoked",
            _ => "NotInstalled"
        };
    }

}
