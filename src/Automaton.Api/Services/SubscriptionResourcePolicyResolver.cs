using Automaton.Api.Entities;
using Automaton.Api.Models;

namespace Automaton.Api.Services;

public enum SubscriptionResourcePolicyResolutionFailure
{
    NoEffectivePlan,
    CapabilityDisabled,
    SpecificationNotConfigured,
    SpecificationNotFound,
    SpecificationInactive,
    InvalidSpecificationResources
}

public sealed record SubscriptionResourcePolicyResolution(
    SubscriptionResourcePolicy? Policy,
    HostedRunnerSpecification? Specification,
    SubscriptionResourcePolicyResolutionFailure? Failure)
{
    public bool Succeeded => Policy != null && Specification != null && Failure == null;

    public static SubscriptionResourcePolicyResolution Success(
        SubscriptionResourcePolicy policy,
        HostedRunnerSpecification specification)
        => new(policy, specification, null);

    public static SubscriptionResourcePolicyResolution Failed(
        SubscriptionResourcePolicyResolutionFailure failure)
        => new(null, null, failure);
}

public interface ISubscriptionResourcePolicyResolver
{
    Task<SubscriptionResourcePolicyResolution> ResolveAsync(
        Guid userId,
        Guid? planId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves the effective subscription policy through the existing plan
/// capability and administrator-managed Hosted Runner specification.
/// </summary>
public sealed class SubscriptionResourcePolicyResolver(
    IHostedRunnerSpecificationResolver specificationResolver) : ISubscriptionResourcePolicyResolver
{
    public async Task<SubscriptionResourcePolicyResolution> ResolveAsync(
        Guid userId,
        Guid? planId,
        CancellationToken cancellationToken)
    {
        var specificationResolution = await specificationResolver.ResolveAsync(userId, planId, cancellationToken);
        if (!specificationResolution.Succeeded)
        {
            return SubscriptionResourcePolicyResolution.Failed(MapFailure(
                specificationResolution.Failure
                ?? HostedRunnerSpecificationResolutionFailure.SpecificationNotConfigured));
        }

        var specification = specificationResolution.Specification!;
        var resources = specification.Resources;
        if (resources is null || !HasValidResourceValues(resources))
        {
            return SubscriptionResourcePolicyResolution.Failed(
                SubscriptionResourcePolicyResolutionFailure.InvalidSpecificationResources);
        }

        return SubscriptionResourcePolicyResolution.Success(
            new SubscriptionResourcePolicy(
                resources.CpuLimit,
                resources.MemoryLimitMb,
                resources.DiskLimitMb,
                resources.PidsLimit,
                SubscriptionResourcePolicy.CurrentMaxHostedRunnersPerUser,
                resources.MaximumApps),
            specification);
    }

    private static bool HasValidResourceValues(HostedRunnerResources resources)
        => resources.CpuLimit is >= HostedRunnerSpecificationLimits.MinimumCpuLimit
            and <= HostedRunnerSpecificationLimits.MaximumCpuLimit
            && resources.MemoryLimitMb is >= HostedRunnerSpecificationLimits.MinimumMemoryLimitMb
            and <= HostedRunnerSpecificationLimits.MaximumMemoryLimitMb
            && resources.MemoryReservationMb >= HostedRunnerSpecificationLimits.MinimumMemoryReservationMb
            && resources.MemoryReservationMb <= resources.MemoryLimitMb
            && resources.DiskLimitMb is >= HostedRunnerSpecificationLimits.MinimumDiskLimitMb
            and <= HostedRunnerSpecificationLimits.MaximumDiskLimitMb
            && resources.PidsLimit is >= HostedRunnerSpecificationLimits.MinimumPidsLimit
            and <= HostedRunnerSpecificationLimits.MaximumPidsLimit
            && resources.SharedMemoryMb is >= HostedRunnerSpecificationLimits.MinimumSharedMemoryMb
            and <= HostedRunnerSpecificationLimits.MaximumSharedMemoryMb
            && resources.DockerLogMaxSizeMb is >= HostedRunnerSpecificationLimits.MinimumDockerLogMaxSizeMb
            and <= HostedRunnerSpecificationLimits.MaximumDockerLogMaxSizeMb
            && resources.DockerLogMaxFiles is >= HostedRunnerSpecificationLimits.MinimumDockerLogMaxFiles
            and <= HostedRunnerSpecificationLimits.MaximumDockerLogMaxFiles
            && resources.MaximumApps is >= HostedRunnerSpecificationLimits.MinimumApps
            and <= HostedRunnerSpecificationLimits.MaximumApps;

    private static SubscriptionResourcePolicyResolutionFailure MapFailure(
        HostedRunnerSpecificationResolutionFailure failure)
        => failure switch
        {
            HostedRunnerSpecificationResolutionFailure.NoEffectivePlan => SubscriptionResourcePolicyResolutionFailure.NoEffectivePlan,
            HostedRunnerSpecificationResolutionFailure.CapabilityDisabled => SubscriptionResourcePolicyResolutionFailure.CapabilityDisabled,
            HostedRunnerSpecificationResolutionFailure.SpecificationNotConfigured => SubscriptionResourcePolicyResolutionFailure.SpecificationNotConfigured,
            HostedRunnerSpecificationResolutionFailure.SpecificationNotFound => SubscriptionResourcePolicyResolutionFailure.SpecificationNotFound,
            HostedRunnerSpecificationResolutionFailure.SpecificationInactive => SubscriptionResourcePolicyResolutionFailure.SpecificationInactive,
            _ => SubscriptionResourcePolicyResolutionFailure.SpecificationNotConfigured
        };
}
