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
    // Normal provisioning reuses the user's existing non-disabled Hosted Runner.
    // This describes that current lifecycle; this policy value does not add a new
    // Hosted Runner admission check.
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
