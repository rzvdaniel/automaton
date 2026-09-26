using Automaton.Api.Entities;

namespace Automaton.Api.Models;

/// <summary>
/// Effective resource limits resolved for a user's current subscription.
/// Hosted Runner specifications remain the source of these values during the
/// transition to a subscription-owned policy.
/// </summary>
public sealed record SubscriptionResourcePolicy(
    decimal CpuLimitCores,
    int MemoryLimitMb,
    int DiskLimitMb,
    int TasksLimit,
    int MaxHostedRunners,
    int MaxApplications)
{
    // One non-deleted managed Hosted Runner record is allowed per account. A
    // sole disabled record is reused; provisioning enforces admission under an
    // account-scoped database lock.
    public const int CurrentMaxHostedRunnersPerUser = 1;

    /// <summary>
    /// Applies policy-owned values to a copy of the backing specification's
    /// resources, preserving existing runner-only settings such as log limits
    /// and shared-memory size in the persisted snapshot.
    /// </summary>
    public HostedRunnerResources ApplyTo(HostedRunnerResources specificationResources)
    {
        var resources = HostedRunnerSpecificationSnapshotFactory.CloneResources(specificationResources);
        resources.CpuLimit = CpuLimitCores;
        resources.MemoryLimitMb = MemoryLimitMb;
        resources.DiskLimitMb = DiskLimitMb;
        resources.PidsLimit = TasksLimit;
        resources.MaximumApps = MaxApplications;
        return resources;
    }
}
