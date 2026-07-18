using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public enum HostedRunnerSpecificationResolutionFailure
{
    NoEffectivePlan,
    CapabilityDisabled,
    SpecificationNotConfigured,
    SpecificationNotFound,
    SpecificationInactive
}

public sealed record HostedRunnerSpecificationResolution(
    HostedRunnerSpecification? Specification,
    HostedRunnerSpecificationResolutionFailure? Failure)
{
    public bool Succeeded => Specification != null && Failure == null;

    public static HostedRunnerSpecificationResolution Success(HostedRunnerSpecification specification)
        => new(specification, null);

    public static HostedRunnerSpecificationResolution Failed(HostedRunnerSpecificationResolutionFailure failure)
        => new(null, failure);
}

public interface IHostedRunnerSpecificationResolver
{
    Task<HostedRunnerSpecificationResolution> ResolveAsync(Guid userId, Guid? planId, CancellationToken cancellationToken);
    Task<HostedRunnerSpecificationResolution> ValidateAsync(HostedRunnerCapability capability, CancellationToken cancellationToken);
}

public sealed class HostedRunnerSpecificationResolver(ApplicationDbContext db) : IHostedRunnerSpecificationResolver
{
    public async Task<HostedRunnerSpecificationResolution> ResolveAsync(Guid userId, Guid? planId, CancellationToken cancellationToken)
    {
        var userPlan = planId.HasValue
            ? await db.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId.Value, cancellationToken)
            : await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => x.SubscriptionPlan).SingleOrDefaultAsync(cancellationToken);

        if (userPlan == null || !userPlan.IsActive)
            return HostedRunnerSpecificationResolution.Failed(HostedRunnerSpecificationResolutionFailure.NoEffectivePlan);

        var capabilities = PlanCapabilitiesSerializer.Deserialize(userPlan.CapabilitiesJson);
        return await ValidateAsync(capabilities.HostedRunner, cancellationToken);
    }

    public async Task<HostedRunnerSpecificationResolution> ValidateAsync(
        HostedRunnerCapability capability,
        CancellationToken cancellationToken)
    {
        if (!capability.Enabled)
            return HostedRunnerSpecificationResolution.Failed(HostedRunnerSpecificationResolutionFailure.CapabilityDisabled);

        if (!capability.SpecificationId.HasValue)
            return HostedRunnerSpecificationResolution.Failed(HostedRunnerSpecificationResolutionFailure.SpecificationNotConfigured);

        var specification = await db.HostedRunnerSpecifications
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == capability.SpecificationId.Value, cancellationToken);

        if (specification == null)
            return HostedRunnerSpecificationResolution.Failed(HostedRunnerSpecificationResolutionFailure.SpecificationNotFound);

        if (!specification.IsActive)
            return HostedRunnerSpecificationResolution.Failed(HostedRunnerSpecificationResolutionFailure.SpecificationInactive);

        return HostedRunnerSpecificationResolution.Success(specification);
    }
}
