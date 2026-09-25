using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

/// <summary>
/// Resolves the account's effective Hosted Runner policy and translates invalid policy state into actionable errors.
/// </summary>
public sealed class HostedRunnerProvisioningPolicyService(
    ApplicationDbContext db,
    ISubscriptionResourcePolicyResolver resourcePolicyResolver,
    ILogger<HostedRunnerProvisioningPolicyService> logger)
{
    public async Task<SubscriptionResourcePolicyResolution> ResolveAsync(
        Guid userId,
        Guid planId,
        CancellationToken cancellationToken)
    {
        var platformSettings = await db.HostedRunnerPlatformSettings.AsNoTracking().SingleOrDefaultAsync(cancellationToken)
            ?? new HostedRunnerPlatformSettings { ProvisioningEnabled = true };
        if (!platformSettings.ProvisioningEnabled)
            throw new InvalidOperationException("Hosted runner provisioning is paused by platform settings.");

        var policyResolution = await resourcePolicyResolver.ResolveAsync(
            userId,
            planId == Guid.Empty ? null : planId,
            cancellationToken);
        if (!policyResolution.Succeeded)
            ThrowPolicyResolutionFailure(userId, policyResolution.Failure);

        return policyResolution;
    }

    private void ThrowPolicyResolutionFailure(
        Guid userId,
        SubscriptionResourcePolicyResolutionFailure? policyFailure)
    {
        var failure = policyFailure ?? SubscriptionResourcePolicyResolutionFailure.SpecificationNotConfigured;
        var message = failure switch
        {
            SubscriptionResourcePolicyResolutionFailure.NoEffectivePlan => "Your subscription plan does not include a hosted runner.",
            SubscriptionResourcePolicyResolutionFailure.CapabilityDisabled => "Your subscription plan does not include a hosted runner.",
            SubscriptionResourcePolicyResolutionFailure.SpecificationNotConfigured => "The hosted runner configuration for your subscription plan is incomplete.",
            SubscriptionResourcePolicyResolutionFailure.SpecificationNotFound => "The hosted runner specification configured for your subscription plan no longer exists.",
            SubscriptionResourcePolicyResolutionFailure.SpecificationInactive => "The hosted runner specification configured for your subscription plan is inactive.",
            SubscriptionResourcePolicyResolutionFailure.InvalidSpecificationResources => "The hosted runner specification configured for your subscription plan has invalid resource limits.",
            _ => "The hosted runner configuration for your subscription plan is invalid."
        };

        if (failure is SubscriptionResourcePolicyResolutionFailure.SpecificationNotConfigured
            or SubscriptionResourcePolicyResolutionFailure.SpecificationNotFound
            or SubscriptionResourcePolicyResolutionFailure.SpecificationInactive
            or SubscriptionResourcePolicyResolutionFailure.InvalidSpecificationResources)
        {
            logger.LogError("Hosted runner specification resolution failed for user {UserId}: {Failure}", userId, failure);
        }

        throw new InvalidOperationException(message);
    }
}
