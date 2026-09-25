using System.Data;
using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Constants;
using Automaton.Messaging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Services;

public sealed class AdminSubscriptionPlanOperationsService(
    ApplicationDbContext dbContext,
    HostedRunnerProvisioningService hostedRunnerProvisioningService,
    HostedRunnerPlanCapabilityValidator capabilityValidator,
    IAuditEventService auditEventService,
    ITransactionalEventExecutor transactionalEventExecutor)
{
    public Task<string?> ValidateCapabilitiesAsync(PlanCapabilities capabilities, CancellationToken cancellationToken)
        => capabilityValidator.ValidateAsync(capabilities, cancellationToken);

    public async Task<(SubscriptionPlan? Plan, string? Error)> CreateAsync(
        CreateSubscriptionPlanRequest request,
        CancellationToken cancellationToken)
    {
        var capabilityError = await capabilityValidator.ValidateAsync(request.Capabilities, cancellationToken);
        if (capabilityError is not null)
        {
            return (null, capabilityError);
        }

        var isActive = request.IsActive;
        var isDefault = isActive && request.IsDefault;
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(
                isDefault ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted,
                cancellationToken)
            : null;

        if (isDefault)
        {
            await dbContext.SubscriptionPlans
                .Where(plan => plan.IsDefault)
                .ExecuteUpdateAsync(update => update.SetProperty(plan => plan.IsDefault, false), cancellationToken);
        }

        var plan = CreatePlanEntity(request, isActive, isDefault);
        dbContext.SubscriptionPlans.Add(plan);
        await dbContext.SaveChangesAsync(cancellationToken);
        await WritePlanCreatedAuditAsync(plan, cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return (plan, null);
    }

    public async Task<PlanAssignmentResult> AssignAsync(
        Guid userId,
        Guid? planId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return new PlanAssignmentResult(PlanAssignmentFailure.UserNotFound);
        }

        SubscriptionPlan? plan = null;
        if (planId.HasValue)
        {
            plan = await dbContext.SubscriptionPlans.FirstOrDefaultAsync(candidate => candidate.Id == planId.Value, cancellationToken);
            if (plan is null)
            {
                return new PlanAssignmentResult(PlanAssignmentFailure.PlanNotFound);
            }

            var capabilityError = await capabilityValidator.ValidateAsync(
                PlanCapabilitiesSerializer.Deserialize(plan.CapabilitiesJson), cancellationToken);
            if (capabilityError is not null)
            {
                return new PlanAssignmentResult(PlanAssignmentFailure.InvalidCapability, capabilityError);
            }
        }

        await CompleteAssignmentAsync(user, plan, planId);
        return new PlanAssignmentResult(PlanAssignmentFailure.None);
    }

    public async Task<bool> SetHostedRunnerTestAccessAsync(Guid userId, bool enabled)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId);
        if (user is null)
        {
            return false;
        }

        user.CanAccessHostedRunnerTestPlans = enabled;
        await dbContext.SaveChangesAsync();
        await auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.HostedRunnerTestPlanAccessChanged,
            Action = AuditActions.Changed,
            EntityType = AuditEntityTypes.User,
            EntityId = user.Id,
            TargetUserId = user.Id,
            Message = $"Hosted runner test-plan access for user '{user.Email ?? user.Id.ToString()}' was set to {enabled}",
            Metadata = new { user.Email, Enabled = enabled }
        });

        return true;
    }

    private Task CompleteAssignmentAsync(ApplicationUser user, SubscriptionPlan? plan, Guid? planId)
        => transactionalEventExecutor.ExecuteAsync(async durableCancellationToken =>
        {
            user.SubscriptionPlanId = planId;
            await dbContext.SaveChangesAsync(durableCancellationToken);

            var hostedRunnerEnabled = plan is not null &&
                PlanCapabilitiesSerializer.Deserialize(plan.CapabilitiesJson).HostedRunner.Enabled;
            if (hostedRunnerEnabled)
            {
                await hostedRunnerProvisioningService.EnsureHostedRunnerAsync(user.Id, durableCancellationToken);
            }
            else
            {
                await hostedRunnerProvisioningService.DisableHostedRunnersForUserAsync(
                    user.Id, "Admin removed subscription plan.", durableCancellationToken);
            }

            await auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                EventType = AuditEventTypes.SubscriptionPlanAssignedToUser,
                Action = AuditActions.Assigned,
                EntityType = AuditEntityTypes.User,
                EntityId = user.Id,
                TargetUserId = user.Id,
                Message = planId.HasValue
                    ? $"Subscription plan '{plan!.Name}' was assigned to user '{user.Email ?? user.Id.ToString()}'"
                    : $"Subscription plan was removed from user '{user.Email ?? user.Id.ToString()}'",
                Metadata = new
                {
                    user.Email,
                    PlanId = planId,
                    PlanName = plan?.Name,
                    HostedRunnerProvisioningTriggered = planId.HasValue
                },
                FailOnError = true
            }, durableCancellationToken);
        }, CancellationToken.None);

    private Task WritePlanCreatedAuditAsync(SubscriptionPlan plan, CancellationToken cancellationToken)
        => auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.SubscriptionPlanCreated,
            Action = AuditActions.Created,
            EntityType = AuditEntityTypes.SubscriptionPlan,
            EntityId = plan.Id,
            Message = $"Subscription plan '{plan.Name}' was created",
            Metadata = new
            {
                plan.Name,
                plan.Description,
                plan.PriceUsd,
                plan.IsDefault,
                plan.IsActive,
                plan.SortOrder,
                HasStripeProductId = !string.IsNullOrWhiteSpace(plan.StripeProductId),
                HasStripePriceId = !string.IsNullOrWhiteSpace(plan.StripePriceId)
            },
            FailOnError = true
        }, cancellationToken);

    private static SubscriptionPlan CreatePlanEntity(
        CreateSubscriptionPlanRequest request,
        bool isActive,
        bool isDefault)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            PriceUsd = request.PriceUsd,
            StripeProductId = request.StripeProductId,
            StripePriceId = request.StripePriceId,
            IsDefault = isDefault,
            IsActive = isActive,
            CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(request.Capabilities),
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow
        };
}

public enum PlanAssignmentFailure
{
    None,
    UserNotFound,
    PlanNotFound,
    InvalidCapability
}

public sealed record PlanAssignmentResult(PlanAssignmentFailure Failure, string? Error = null);
