using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Automaton.Core.Enums;

namespace Automaton.Api.Controllers.Admin;

internal static class HostedRunnerAdminResponseMapper
{
    public static HostedRunnerResponse ToResponse(HostedRunner runner)
    {
        var deployment = runner.Deployments.OrderByDescending(x => x.StartedAt).FirstOrDefault();
        var verification = runner.StorageVerifications.OrderByDescending(x => x.RequestedAtUtc).FirstOrDefault();
        var runnerConnectivityStatus = runner.Runner == null
            ? RunnerStatus.None.ToString()
            : RunnerPresenceEvaluator.DeriveStatus(runner.Runner.ConnectionId, runner.Runner.LastHeartbeatAtUtc, DateTime.UtcNow).ToString();

        return new HostedRunnerResponse
        {
            Id = runner.Id,
            UserId = runner.UserId,
            UserEmail = runner.User.Email ?? string.Empty,
            HostedRunnerSpecificationId = runner.HostedRunnerSpecificationId,
            SpecificationRevision = runner.SpecificationRevision,
            SpecificationSnapshot = runner.SpecificationSnapshot,
            PendingSpecificationSnapshot = runner.PendingSpecificationSnapshot,
            RunnerHostId = runner.RunnerHostId,
            RunnerHostName = runner.RunnerHost?.Name,
            RunnerId = runner.RunnerId,
            RunnerName = runner.Runner?.Name,
            ApiKeyId = runner.ApiKeyId,
            Name = runner.Name,
            Status = runner.Status,
            ContainerName = runner.ContainerName,
            ContainerId = runner.ContainerId,
            RuntimePort = runner.RuntimePort,
            RunnerConnectivityStatus = runnerConnectivityStatus,
            RunnerLastHeartbeatAtUtc = runner.Runner?.LastHeartbeatAtUtc,
            RunnerLastConnectedAtUtc = runner.Runner?.LastConnectedAtUtc,
            RunnerVersion = runner.Runner?.Version,
            RunnerMachineName = runner.Runner?.MachineName,
            LastError = runner.LastError,
            CreatedAt = runner.CreatedAt,
            UpdatedAt = runner.UpdatedAt,
            ProvisionedAt = runner.ProvisionedAt,
            DeletedAt = runner.DeletedAt,
            StorageAllowanceBytes = runner.StorageAllowanceBytes,
            StorageMeasuredAtUtc = runner.StorageMeasuredAtUtc,
            StorageMeasurementSource = runner.StorageMeasurementSource,
            StorageMeasurementStatus = runner.StorageMeasurementStatus,
            StorageSystemBytes = runner.StorageSystemBytes,
            StorageUserBytes = runner.StorageUserBytes,
            StorageTotalBytes = runner.StorageTotalBytes,
            StorageUsagePercent = runner.StorageUsagePercent,
            StorageState = runner.StorageState,
            StorageMeasurementError = runner.StorageMeasurementError,
            StorageVerificationRequestedAtUtc = runner.StorageVerificationRequestedAtUtc,
            StorageVerifiedAtUtc = runner.StorageVerifiedAtUtc,
            StorageVerifiedTotalBytes = runner.StorageVerifiedTotalBytes,
            StorageSelfReportedAtUtc = runner.StorageSelfReportedAtUtc,
            StorageSelfReportedTotalBytes = runner.StorageSelfReportedTotalBytes,
            StorageSelfReportedStatus = runner.StorageSelfReportedStatus,
            StorageSelfReportedError = runner.StorageSelfReportedError,
            HostStorageAvailableBytes = runner.RunnerHost?.StorageAvailableBytes,
            HostStorageReserveRequiredBytes = runner.RunnerHost?.StorageReserveRequiredBytes,
            HostStorageState = runner.RunnerHost?.StorageState,
            StorageVerificationStatus = verification?.Status,
            StorageVerificationReason = verification?.Reason,
            StorageVerificationError = verification?.Error,
            EnforcementState = runner.EnforcementState,
            OverLimitSinceUtc = runner.OverLimitSinceUtc,
            GracePeriodEndsAtUtc = runner.GracePeriodEndsAtUtc,
            SuspendedAtUtc = runner.SuspendedAtUtc,
            RecoveredAtUtc = runner.RecoveredAtUtc,
            LastEnforcementReason = runner.LastEnforcementReason,
            LastNotificationAtUtc = runner.LastNotificationAtUtc,
            LatestDeployment = ToDeploymentResponse(deployment)
        };
    }

    private static HostedRunnerDeploymentResponse? ToDeploymentResponse(HostedRunnerDeployment? deployment)
        => deployment == null
            ? null
            : new HostedRunnerDeploymentResponse
            {
                Id = deployment.Id,
                Action = deployment.Action,
                Status = deployment.Status,
                CommandPreview = deployment.CommandPreview,
                Logs = deployment.Logs,
                Error = deployment.Error,
                StartedAt = deployment.StartedAt,
                FinishedAt = deployment.FinishedAt
            };
}

public sealed record DeleteHostedRunnerRequest(string DataDisposition);
public sealed record ExtendStorageGracePeriodRequest(int Hours);

public record RunnerHostResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string? Region { get; init; }
    public string? PublicIp { get; init; }
    public string? PrivateIp { get; init; }
    public string? ProviderResourceId { get; init; }
    public string? ProviderServerType { get; init; }
    public string? ProviderImageId { get; init; }
    public string ProvisioningTransport { get; init; } = RunnerHostProvisioningTransports.InfrastructureRunner;
    public string RuntimeConnectivityMode { get; init; } = RunnerHostRuntimeConnectivityModes.PublicHostPort;
    public bool HostedRunnerSchedulingEnabled { get; init; } = true;
    public string Status { get; init; } = RunnerHostStatuses.Active;
    public string ProvisioningStatus { get; init; } = RunnerHostProvisioningStatuses.Manual;
    public DateTime? ProvisioningStartedAtUtc { get; init; }
    public DateTime? InfrastructureRunnerEnrolledAtUtc { get; init; }
    public DateTime? ValidatedAtUtc { get; init; }
    public DateTime? ApprovedAtUtc { get; init; }
    public Guid? ApprovedByUserId { get; init; }
    public string? ProvisioningFailureCode { get; init; }
    public string? ProvisioningFailureMessage { get; init; }
    public bool IsSchedulingEligible { get; init; }
    public decimal TotalCpuCores { get; init; }
    public decimal ReservedCpuCores { get; init; }
    public decimal UsedCpuCores { get; init; }
    public int TotalMemoryMb { get; init; }
    public int ReservedMemoryMb { get; init; }
    public int UsedMemoryMb { get; init; }
    public int TotalDiskMb { get; init; }
    public int ReservedDiskMb { get; init; }
    public int UsedDiskMb { get; init; }
    public DateTime? StorageMeasuredAtUtc { get; init; }
    public long? StorageAvailableBytes { get; init; }
    public long? StorageReserveRequiredBytes { get; init; }
    public string StorageState { get; init; } = RunnerHostStorageStates.Unknown;
    public string StorageMeasurementStatus { get; init; } = StorageMeasurementStatuses.NeverMeasured;
    public string? StorageMeasurementError { get; init; }
    public int HostedRunnerCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public Guid? InfrastructureRunnerId { get; init; }
    public string? InfrastructureRunnerName { get; init; }
    public string? InfrastructureRunnerPurpose { get; init; }
    public string InfrastructureRunnerStatus { get; init; } = RunnerStatus.None.ToString();
    public DateTime? InfrastructureRunnerLastHeartbeatAtUtc { get; init; }
    public string InfrastructureRunnerOnboardingStatus { get; init; } = "NotInstalled";
    public string? InfrastructureRunnerVersion { get; init; }
    public string? InfrastructureRunnerImageDigest { get; init; }
    public int? InfrastructureRunnerSpecificationVersion { get; init; }
    public string? InfrastructureRunnerApiCompatibilityVersion { get; init; }
    public string? InfrastructureRunnerOperatingSystem { get; init; }
    public string? InfrastructureRunnerArchitecture { get; init; }
    public string? InfrastructureRunnerContainerHealth { get; init; }
    public string? InfrastructureRunnerInstallationId { get; init; }
    public IReadOnlyList<string> InfrastructureRunnerCapabilities { get; init; } = Array.Empty<string>();
    public DateTime? InfrastructureEnrollmentExpiresAtUtc { get; init; }
    public DateTime? LastHeartbeatAtUtc { get; init; }
    public string? MetadataJson { get; init; }
    public string? HealthJson { get; init; }
}

public record UpdateRunnerHostRequest
{
    public string? Name { get; init; }
    public string? Provider { get; init; }
    public string? Region { get; init; }
    public string? PublicIp { get; init; }
    public string? PrivateIp { get; init; }
    public string? ProvisioningTransport { get; init; }
    public string? RuntimeConnectivityMode { get; init; }
    public bool? HostedRunnerSchedulingEnabled { get; init; }
    public string? Status { get; init; }
    public decimal? TotalCpuCores { get; init; }
    public decimal? ReservedCpuCores { get; init; }
    public int? TotalMemoryMb { get; init; }
    public int? ReservedMemoryMb { get; init; }
    public int? TotalDiskMb { get; init; }
    public int? ReservedDiskMb { get; init; }
}

public record HostedRunnerResponse
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string UserEmail { get; init; } = string.Empty;
    public Guid HostedRunnerSpecificationId { get; init; }
    public int SpecificationRevision { get; init; }
    public HostedRunnerSpecificationSnapshot SpecificationSnapshot { get; init; } = new();
    public HostedRunnerSpecificationSnapshot? PendingSpecificationSnapshot { get; init; }
    public Guid? RunnerHostId { get; init; }
    public string? RunnerHostName { get; init; }
    public Guid? RunnerId { get; init; }
    public string? RunnerName { get; init; }
    public Guid? ApiKeyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string? ContainerId { get; init; }
    public int? RuntimePort { get; init; }
    public string RunnerConnectivityStatus { get; init; } = RunnerStatus.None.ToString();
    public DateTime? RunnerLastHeartbeatAtUtc { get; init; }
    public DateTime? RunnerLastConnectedAtUtc { get; init; }
    public string? RunnerVersion { get; init; }
    public string? RunnerMachineName { get; init; }
    public string? LastError { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? ProvisionedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
    public HostedRunnerDeploymentResponse? LatestDeployment { get; init; }
    public long StorageAllowanceBytes { get; init; }
    public DateTime? StorageMeasuredAtUtc { get; init; }
    public string StorageMeasurementSource { get; init; } = StorageMeasurementSources.RunnerSelfReported;
    public string StorageMeasurementStatus { get; init; } = StorageMeasurementStatuses.NeverMeasured;
    public long? StorageSystemBytes { get; init; }
    public long? StorageUserBytes { get; init; }
    public long? StorageTotalBytes { get; init; }
    public decimal? StorageUsagePercent { get; init; }
    public string StorageState { get; init; } = HostedRunnerStorageStates.Unknown;
    public string? StorageMeasurementError { get; init; }
    public DateTime? StorageVerificationRequestedAtUtc { get; init; }
    public DateTime? StorageVerifiedAtUtc { get; init; }
    public long? StorageVerifiedTotalBytes { get; init; }
    public DateTime? StorageSelfReportedAtUtc { get; init; }
    public long? StorageSelfReportedTotalBytes { get; init; }
    public string StorageSelfReportedStatus { get; init; } = StorageMeasurementStatuses.NeverMeasured;
    public string? StorageSelfReportedError { get; init; }
    public long? HostStorageAvailableBytes { get; init; }
    public long? HostStorageReserveRequiredBytes { get; init; }
    public string? HostStorageState { get; init; }
    public string? StorageVerificationStatus { get; init; }
    public string? StorageVerificationReason { get; init; }
    public string? StorageVerificationError { get; init; }
    public string EnforcementState { get; init; } = HostedRunnerEnforcementStates.None;
    public DateTime? OverLimitSinceUtc { get; init; }
    public DateTime? GracePeriodEndsAtUtc { get; init; }
    public DateTime? SuspendedAtUtc { get; init; }
    public DateTime? RecoveredAtUtc { get; init; }
    public string? LastEnforcementReason { get; init; }
    public DateTime? LastNotificationAtUtc { get; init; }
}

public record HostedRunnerDeploymentResponse
{
    public Guid Id { get; init; }
    public string Action { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? CommandPreview { get; init; }
    public string? Logs { get; init; }
    public string? Error { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
}

public record ProvisionHostedRunnerRequest
{
}
