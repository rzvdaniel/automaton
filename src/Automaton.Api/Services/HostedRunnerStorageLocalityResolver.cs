using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Resolves account storage host locality from its durable allocation and
/// unambiguous Hosted Runner history. It never selects or migrates storage.
/// </summary>
public sealed class HostedRunnerStorageLocalityResolver(ApplicationDbContext db)
{
    public async Task<Guid?> ResolveAsync(
        Guid accountId,
        AccountStorageAllocation allocation,
        CancellationToken cancellationToken)
    {
        var runnerLocations = await db.HostedRunners
            .AsNoTracking()
            .Where(runner => runner.UserId == accountId)
            .Select(runner => new HostedRunnerStorageLocation(
                runner.Id,
                runner.RunnerHostId,
                runner.StorageProjectId,
                runner.DeletedAt,
                runner.ContainerId,
                runner.RunnerId))
            .ToListAsync(cancellationToken);

        EnsureStorageLocationsHaveKnownHosts(accountId, allocation.StorageProjectId, runnerLocations);
        var historicalHostId = ResolveHistoricalStorageHostId(accountId, allocation, runnerLocations);
        return allocation.StorageRunnerHostId ?? historicalHostId;
    }

    private static void EnsureStorageLocationsHaveKnownHosts(
        Guid accountId,
        int storageProjectId,
        IReadOnlyCollection<HostedRunnerStorageLocation> locations)
    {
        if (locations.Any(location => location.RunnerHostId is null && HasStorageIdentity(location, storageProjectId)))
        {
            throw new InvalidOperationException(
                $"Account '{accountId}' has Hosted Runner storage whose Runner Host is unknown. An administrator must review the existing storage location before provisioning.");
        }
    }

    private static bool HasStorageIdentity(HostedRunnerStorageLocation location, int storageProjectId)
        => location.ContainerId is not null
            || location.RunnerId is not null
            || location.DeletedAt is not null && location.StorageProjectId == storageProjectId;

    private static Guid? ResolveHistoricalStorageHostId(
        Guid accountId,
        AccountStorageAllocation allocation,
        IReadOnlyCollection<HostedRunnerStorageLocation> locations)
    {
        var relevantLocations = locations.Where(location => location.RunnerHostId.HasValue
                && IsRelevantStorageLocation(location, allocation.StorageProjectId))
            .ToArray();
        var historicalHostIds = relevantLocations
            .Select(location => location.RunnerHostId!.Value)
            .Distinct()
            .ToArray();
        if (historicalHostIds.Length > 1)
        {
            var runnerIds = string.Join(", ", relevantLocations.Select(location => location.Id));
            throw new InvalidOperationException(
                $"Account '{accountId}' has Hosted Runner storage on multiple Runner Hosts ({string.Join(", ", historicalHostIds)}; Runners: {runnerIds}). Storage migration is required before provisioning.");
        }

        var historicalHostId = historicalHostIds.SingleOrDefault();
        if (historicalHostId == Guid.Empty)
            return null;
        if (allocation.StorageRunnerHostId is Guid allocatedHostId && historicalHostId != allocatedHostId)
        {
            throw new InvalidOperationException(
                $"Account '{accountId}' has Hosted Runner storage on Runner Host {historicalHostId}, " +
                $"but its account allocation is pinned to Runner Host {allocatedHostId}. Storage migration is required before provisioning.");
        }

        return historicalHostId;
    }

    private static bool IsRelevantStorageLocation(HostedRunnerStorageLocation location, int storageProjectId)
        => location.DeletedAt is null
            || location.StorageProjectId == storageProjectId
            || location.ContainerId is not null
            || location.RunnerId is not null;

    private sealed record HostedRunnerStorageLocation(
        Guid Id,
        Guid? RunnerHostId,
        int? StorageProjectId,
        DateTime? DeletedAt,
        string? ContainerId,
        Guid? RunnerId);
}
