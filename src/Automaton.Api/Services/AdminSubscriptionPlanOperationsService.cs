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
    IAuditEventService auditEventService,
    ITransactionalEventExecutor transactionalEventExecutor)
{
    public async Task<SubscriptionPlan> CreateAsync(
        CreateSubscriptionPlanRequest request,
        CancellationToken cancellationToken)
    {
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

        return plan;
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

        }

        await CompleteAssignmentAsync(user, plan, planId);
        return new PlanAssignmentResult(PlanAssignmentFailure.None);
    }

    private Task CompleteAssignmentAsync(ApplicationUser user, SubscriptionPlan? plan, Guid? planId)
        => transactionalEventExecutor.ExecuteAsync(async durableCancellationToken =>
        {
            user.SubscriptionPlanId = planId;
            await dbContext.SaveChangesAsync(durableCancellationToken);

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
                    PlanName = plan?.Name
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
    PlanNotFound
}

public sealed record PlanAssignmentResult(PlanAssignmentFailure Failure, string? Error = null);
