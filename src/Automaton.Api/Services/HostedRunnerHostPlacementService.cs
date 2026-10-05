using Automaton.Api.Config;
using Automaton.Api.Constants;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;

namespace Automaton.Api.Services;

/// <summary>
/// Selects eligible Runner Hosts and allocates the runtime connection details for a Hosted Runner.
/// </summary>
public sealed class HostedRunnerHostPlacementService(
    ApplicationDbContext db,
    IOptionsSnapshot<HostedRunnerOptions> options,
    IOptions<StorageTelemetryOptions>? storageTelemetryOptions = null)
{
    private readonly HostedRunnerOptions settings = options.Value;
    private readonly StorageTelemetryOptions telemetrySettings = storageTelemetryOptions?.Value ?? new StorageTelemetryOptions();

    public async Task<(RunnerHost? Host, string? FailureReason)> SelectHostAsync(
        Guid accountId,
        HostedRunnerResources requested,
        Guid? excludedHostedRunnerId,
        int accountStorageProjectId,
        Guid? requiredStorageHostId,
        CancellationToken cancellationToken)
    {
        if (requested.WritableLayerLimitMb is < HostedRunnerSpecificationLimits.MinimumWritableLayerLimitMb
            or > HostedRunnerSpecificationLimits.MaximumWritableLayerLimitMb)
            return (null, "The Hosted Runner writable layer limit is outside the supported range.");

        var hasSharedProjectLimitConflict = false;
        var requestedStorageBytes = CalculateDiskReservationMb(requested) * 1024L * 1024L;
        var now = DateTime.UtcNow;
        var hosts = await GetSchedulableHostsAsync(requestedStorageBytes, now, cancellationToken);
        if (requiredStorageHostId.HasValue)
        {
            hosts = hosts.Where(host => host.Id == requiredStorageHostId.Value).ToList();
            if (hosts.Count == 0)
            {
                return (null,
                    $"Account storage is pinned to Runner Host {requiredStorageHostId.Value}, which is not currently schedulable. Storage migration must be completed explicitly before reprovisioning.");
            }
        }

        foreach (var candidate in hosts)
        {
            var selection = await TrySelectHostAsync(
                candidate, accountId, requested, excludedHostedRunnerId, accountStorageProjectId,
                requestedStorageBytes, now, cancellationToken);
            if (selection.Host != null)
                return (selection.Host, null);
            hasSharedProjectLimitConflict |= selection.SharedProjectLimitConflict;
        }

        if (hasSharedProjectLimitConflict)
        {
            return (null,
                "The account already has a Hosted Runner on an eligible host using the same XFS project with a different disk-limit snapshot. " +
                "Move or delete the co-located Hosted Runner before changing the shared project limit.");
        }

        return (null, null);
    }

    public string? BuildRuntimeEndpoint(HostedRunner hostedRunner, RunnerHost host)
    {
        if (UsesLocalDockerNetwork(host))
            return $"http://{hostedRunner.ContainerName}:{settings.RuntimePort}";

        if (string.IsNullOrWhiteSpace(host.PublicIp) || hostedRunner.RuntimePort is not > 0)
            return null;

        var endpointHost = host.PublicIp.Trim();
        if (IPAddress.TryParse(endpointHost, out var address) &&
            address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            endpointHost = $"[{endpointHost}]";
        }

        return $"http://{endpointHost}:{hostedRunner.RuntimePort}";
    }

    public async Task<int?> AllocateRuntimePortAsync(
        RunnerHost host,
        HostedRunner hostedRunner,
        bool keepExistingPort,
        CancellationToken cancellationToken)
    {
        if (UsesLocalDockerNetwork(host))
            return null;

        if (keepExistingPort && hostedRunner.RuntimePort is > 0)
            return hostedRunner.RuntimePort;

        var start = settings.RemoteRuntimePortStart;
        var end = settings.RemoteRuntimePortEnd;
        if (start <= 0 || end < start || end > 65535)
            throw new InvalidOperationException("HostedRunners remote runtime port range is invalid.");

        var usedPorts = await db.HostedRunners
            .Where(x => x.RunnerHostId == host.Id &&
                        x.Id != hostedRunner.Id &&
                        x.DeletedAt == null &&
                        x.RuntimePort != null)
            .Select(x => x.RuntimePort!.Value)
            .ToListAsync(cancellationToken);
        var used = usedPorts.ToHashSet();

        for (var port = start; port <= end; port++)
        {
            if (!used.Contains(port))
                return port;
        }

        throw new InvalidOperationException($"Runner host '{host.Name}' has no available remote runtime ports.");
    }

    private async Task<List<RunnerHost>> GetSchedulableHostsAsync(
        long requestedStorageBytes,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var candidates = await db.RunnerHosts
            .AsNoTracking()
            .Where(x => x.Status == RunnerHostStatuses.Active
                        && x.HostedRunnerSchedulingEnabled
                        && x.ProvisioningStatus == RunnerHostProvisioningStatuses.Ready
                        && x.ValidatedAtUtc != null
                        && x.ApprovedAtUtc != null
                        && x.ProvisioningTransport == RunnerHostProvisioningTransports.InfrastructureRunner
                        && x.InfrastructureRunnerId != null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return candidates.Where(host => StorageTelemetryPolicy.CanScheduleHostedRunners(
                host.StorageState, host.StorageAvailableBytes, host.StorageReserveRequiredBytes ?? long.MaxValue,
                requestedStorageBytes, host.StorageMeasuredAtUtc, now, telemetrySettings))
            .ToList();
    }

    private async Task<(RunnerHost? Host, bool SharedProjectLimitConflict)> TrySelectHostAsync(
        RunnerHost candidate,
        Guid accountId,
        HostedRunnerResources requested,
        Guid? excludedHostedRunnerId,
        int accountStorageProjectId,
        long requestedStorageBytes,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!UsesLocalDockerNetwork(candidate) && string.IsNullOrWhiteSpace(candidate.PublicIp))
            return (null, false);

        if (IsMySqlProvider())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT Id FROM RunnerHosts WHERE Id = {candidate.Id} FOR UPDATE",
                cancellationToken);
        }

        var host = await LoadEligibleHostAsync(candidate.Id, cancellationToken);
        if (host == null || !CanScheduleStorage(host, requestedStorageBytes, now))
            return (null, false);

        if (HasIncompatibleSharedProjectLimit(host, accountId, requested.DiskLimitMb, excludedHostedRunnerId, accountStorageProjectId))
            return (null, true);

        return HasCapacity(host, requested, excludedHostedRunnerId)
            ? (host, false)
            : (null, false);
    }

    private Task<RunnerHost?> LoadEligibleHostAsync(Guid hostId, CancellationToken cancellationToken)
        => db.RunnerHosts
            .Include(x => x.HostedRunners)
            .FirstOrDefaultAsync(
                x => x.Id == hostId
                     && x.Status == RunnerHostStatuses.Active
                     && x.HostedRunnerSchedulingEnabled
                     && x.ProvisioningStatus == RunnerHostProvisioningStatuses.Ready
                     && x.ValidatedAtUtc != null
                     && x.ApprovedAtUtc != null
                     && x.ProvisioningTransport == RunnerHostProvisioningTransports.InfrastructureRunner
                     && x.InfrastructureRunnerId != null,
                cancellationToken);

    private bool CanScheduleStorage(RunnerHost host, long requestedStorageBytes, DateTime now)
        => StorageTelemetryPolicy.CanScheduleHostedRunners(
            host.StorageState, host.StorageAvailableBytes, host.StorageReserveRequiredBytes ?? long.MaxValue,
            requestedStorageBytes, host.StorageMeasuredAtUtc, now, telemetrySettings);

    private static bool HasIncompatibleSharedProjectLimit(
        RunnerHost host,
        Guid accountId,
        int requestedDiskLimitMb,
        Guid? excludedHostedRunnerId,
        int accountStorageProjectId)
        => host.HostedRunners.Any(runner =>
            runner.UserId == accountId
            && runner.Id != excludedHostedRunnerId
            && runner.DeletedAt == null
            && runner.StorageProjectId == accountStorageProjectId
            && (runner.PendingSpecificationSnapshot ?? runner.SpecificationSnapshot).Resources.DiskLimitMb != requestedDiskLimitMb);

    private static bool HasCapacity(
        RunnerHost host,
        HostedRunnerResources requested,
        Guid? excludedHostedRunnerId)
    {
        var occupying = host.HostedRunners.Where(HostedRunnerComputeReservationPolicy.OccupiesCapacity);
        var assigned = occupying.Where(runner => runner.Id != excludedHostedRunnerId);
        var resizing = occupying.FirstOrDefault(runner => runner.Id == excludedHostedRunnerId);
        var resizingCompute = resizing is null ? default : HostedRunnerComputeReservationPolicy.Calculate(resizing);
        var assignedCompute = assigned.Select(HostedRunnerComputeReservationPolicy.Calculate).ToArray();
        return host.ReservedCpuCores + assignedCompute.Sum(reservation => reservation.CpuCores)
                + Math.Max(requested.CpuLimit, resizingCompute.CpuCores) <= host.TotalCpuCores
            && host.ReservedMemoryMb + assignedCompute.Sum(reservation => reservation.MemoryMb)
                + Math.Max(requested.MemoryLimitMb, resizingCompute.MemoryMb) <= host.TotalMemoryMb
            && host.ReservedDiskMb
                + assigned.Sum(runner => CalculateDiskReservationMb(
                    (runner.PendingSpecificationSnapshot ?? runner.SpecificationSnapshot).Resources))
                + CalculateDiskReservationMb(requested) <= EffectiveDiskCapacityMb(host);
    }

    internal static int EffectiveDiskCapacityMb(RunnerHost host)
    {
        if (host.DockerFilesystemTotalBytes is not > 0)
            return 0;
        var measuredCapacityMb = host.DockerFilesystemTotalBytes.Value / (1024L * 1024L);
        return (int)Math.Min(host.TotalDiskMb, Math.Min(measuredCapacityMb, int.MaxValue));
    }

    private static long CalculateDiskReservationMb(HostedRunnerResources resources)
        => (long)resources.DiskLimitMb
            + resources.WritableLayerLimitMb
            + (long)resources.DockerLogMaxSizeMb * resources.DockerLogMaxFiles;

    private bool IsMySqlProvider()
        => db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;

    private static bool UsesLocalDockerNetwork(RunnerHost host)
        => string.Equals(
            host.RuntimeConnectivityMode,
            RunnerHostRuntimeConnectivityModes.LocalDockerNetwork,
            StringComparison.Ordinal);
}
