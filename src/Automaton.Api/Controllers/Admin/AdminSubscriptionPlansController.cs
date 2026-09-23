using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Automaton.Api.Services.Interfaces;
using Automaton.Api.Models;
using Automaton.Api.Constants;
using Automaton.Api.Entities;
using Automaton.Api.Data;
using Automaton.Api.Services;
using Automaton.Messaging.Abstractions;

namespace Automaton.Api.Controllers.Admin;

/// <summary>
/// Admin controller for managing subscription plans and user plan assignments.
/// </summary>
[ApiController]
[Route("api/admin/subscription-plans")]
[Authorize(Policy = "Administrators")]
public class AdminSubscriptionPlansController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly HostedRunnerProvisioningService _hostedRunnerProvisioningService;
    private readonly IHostedRunnerSpecificationResolver _hostedRunnerSpecificationResolver;
    private readonly IAuditEventService _auditEventService;
    private readonly ITransactionalEventExecutor _transactionalEventExecutor;

    public AdminSubscriptionPlansController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        HostedRunnerProvisioningService hostedRunnerProvisioningService,
        IHostedRunnerSpecificationResolver hostedRunnerSpecificationResolver,
        IAuditEventService auditEventService,
        ITransactionalEventExecutor transactionalEventExecutor)
    {
        _db = db;
        _userManager = userManager;
        _hostedRunnerProvisioningService = hostedRunnerProvisioningService;
        _hostedRunnerSpecificationResolver = hostedRunnerSpecificationResolver;
        _auditEventService = auditEventService;
        _transactionalEventExecutor = transactionalEventExecutor;
    }

    #region Plans CRUD

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SubscriptionPlanResponse>>> GetPlans()
    {
        var plans = await _db.SubscriptionPlans
            .AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .Select(p => ToResponse(p))
            .ToListAsync();

        return Ok(plans);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SubscriptionPlanResponse>> GetPlan(Guid id)
    {
        var plan = await _db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (plan == null) return NotFound();
        return Ok(ToResponse(plan));
    }

    [HttpPost]
    public async Task<ActionResult<SubscriptionPlanResponse>> CreatePlan([FromBody] CreateSubscriptionPlanRequest req, CancellationToken cancellationToken)
    {
        var capabilityError = await ValidateHostedRunnerCapabilityAsync(req.Capabilities, cancellationToken);
        if (capabilityError != null) return BadRequest(new { message = capabilityError });

        var isActive = req.IsActive;
        var isDefault = isActive && req.IsDefault;

        await using var transaction = isDefault && _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        // Only one default plan at a time
        if (isDefault)
            await _db.SubscriptionPlans
                .Where(p => p.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDefault, false), cancellationToken);

        var plan = new SubscriptionPlan
        {
            Id                     = Guid.NewGuid(),
            Name                   = req.Name,
            Description            = req.Description,
            PriceUsd               = req.PriceUsd,
            StripeProductId        = req.StripeProductId,
            StripePriceId          = req.StripePriceId,
            IsDefault              = isDefault,
            IsActive               = isActive,
            CapabilitiesJson       = PlanCapabilitiesSerializer.Serialize(req.Capabilities),
            SortOrder              = req.SortOrder,
            CreatedAt              = DateTime.UtcNow
        };

        _db.SubscriptionPlans.Add(plan);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        await _auditEventService.WriteAsync(new AuditEventWriteRequest
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
            }
        }, cancellationToken);

        return CreatedAtAction(nameof(GetPlan), new { id = plan.Id }, ToResponse(plan));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SubscriptionPlanResponse>> UpdatePlan(Guid id, [FromBody] UpdateSubscriptionPlanRequest req, CancellationToken cancellationToken)
    {
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan == null) return NotFound();

        if (req.Capabilities != null)
        {
            var capabilityError = await ValidateHostedRunnerCapabilityAsync(req.Capabilities, cancellationToken);
            if (capabilityError != null) return BadRequest(new { message = capabilityError });
        }

        var newIsActive = req.IsActive ?? plan.IsActive;
        var newIsDefault = (req.IsDefault ?? plan.IsDefault) && newIsActive;

        await using var transaction = newIsDefault && !plan.IsDefault && _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        if (newIsDefault && !plan.IsDefault)
            await _db.SubscriptionPlans
                .Where(p => p.IsDefault && p.Id != id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDefault, false), cancellationToken);

        if (req.Name        != null) plan.Name               = req.Name;
        if (req.Description != null) plan.Description        = req.Description;
        if (req.PriceUsd              .HasValue) plan.PriceUsd               = req.PriceUsd.Value;
        if (req.StripeProductId != null) plan.StripeProductId = req.StripeProductId == "" ? null : req.StripeProductId;
        if (req.StripePriceId   != null) plan.StripePriceId   = req.StripePriceId   == "" ? null : req.StripePriceId;
        if (req.IsActive              .HasValue) plan.IsActive               = req.IsActive.Value;
        plan.IsDefault = newIsDefault;
        if (req.Capabilities != null) plan.CapabilitiesJson = PlanCapabilitiesSerializer.Serialize(req.Capabilities);
        if (req.SortOrder             .HasValue) plan.SortOrder              = req.SortOrder.Value;
        plan.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Ok(ToResponse(plan));
    }

    #endregion

    private async Task<string?> ValidateHostedRunnerCapabilityAsync(PlanCapabilities capabilities, CancellationToken cancellationToken)
    {
        var capability = capabilities.HostedRunner;
        if (!capability.Enabled) return null;

        var resolution = await _hostedRunnerSpecificationResolver.ValidateAsync(capability, cancellationToken);
        return resolution.Failure switch
        {
            HostedRunnerSpecificationResolutionFailure.SpecificationNotConfigured => "A hosted runner specification is required when hosted runners are enabled.",
            HostedRunnerSpecificationResolutionFailure.SpecificationNotFound => "The selected hosted runner specification does not exist.",
            HostedRunnerSpecificationResolutionFailure.SpecificationInactive => "The selected hosted runner specification is inactive.",
            _ => null
        };
    }

    #region User Plan Assignments

    /// <summary>
    /// List all users with their current subscription plan and usage summary.
    /// </summary>
    [HttpGet("users")]
    public async Task<ActionResult<IEnumerable<UserPlanResponse>>> GetUsers()
    {
        var users = await _userManager.Users
            .AsNoTracking()
            .Include(u => u.SubscriptionPlan)
            .OrderBy(u => u.Email)
            .ToListAsync();

        var result = users.Select(u =>
        {
            return new UserPlanResponse
            {
                UserId              = u.Id,
                Email               = u.Email ?? string.Empty,
                Name                = u.Name ?? u.Email ?? string.Empty,
                SubscriptionPlanId  = u.SubscriptionPlanId,
                PlanName            = u.SubscriptionPlan?.Name,
                CanAccessHostedRunnerTestPlans = u.CanAccessHostedRunnerTestPlans
            };
        });

        return Ok(result);
    }

    /// <summary>
    /// Assign a subscription plan to a user. Pass planId = null to remove the plan.
    /// </summary>
    [HttpPost("users/{userId:guid}/assign")]
    public async Task<IActionResult> AssignPlan(Guid userId, [FromBody] AssignPlanRequest req, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user == null) return NotFound("User not found.");

        SubscriptionPlan? plan = null;
        if (req.PlanId.HasValue)
        {
            plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == req.PlanId.Value, cancellationToken);
            if (plan == null) return NotFound("Subscription plan not found.");

            var capabilityError = await ValidateHostedRunnerCapabilityAsync(
                PlanCapabilitiesSerializer.Deserialize(plan.CapabilitiesJson),
                cancellationToken);
            if (capabilityError != null) return BadRequest(new { message = capabilityError });
        }

        var hostedRunnerCapability = plan == null
            ? null
            : PlanCapabilitiesSerializer.Deserialize(plan.CapabilitiesJson).HostedRunner;

        await _transactionalEventExecutor.ExecuteAsync(async durableCancellationToken =>
        {
            user.SubscriptionPlanId = req.PlanId;
            await _db.SaveChangesAsync(durableCancellationToken);

            if (hostedRunnerCapability?.Enabled == true)
            {
                await _hostedRunnerProvisioningService.EnsureHostedRunnerAsync(userId, durableCancellationToken);
            }
            else
            {
                await _hostedRunnerProvisioningService.DisableHostedRunnersForUserAsync(
                    userId, "Admin removed subscription plan.", durableCancellationToken);
            }

            await _auditEventService.WriteAsync(new AuditEventWriteRequest
            {
                EventType = AuditEventTypes.SubscriptionPlanAssignedToUser,
                Action = AuditActions.Assigned,
                EntityType = AuditEntityTypes.User,
                EntityId = user.Id,
                TargetUserId = user.Id,
                Message = req.PlanId.HasValue
                    ? $"Subscription plan '{plan!.Name}' was assigned to user '{user.Email ?? user.Id.ToString()}'"
                    : $"Subscription plan was removed from user '{user.Email ?? user.Id.ToString()}'",
                Metadata = new
                {
                    user.Email,
                    PlanId = req.PlanId,
                    PlanName = plan?.Name,
                    HostedRunnerProvisioningTriggered = req.PlanId.HasValue
                },
                FailOnError = true
            }, durableCancellationToken);
        }, CancellationToken.None);

        return NoContent();
    }

    [HttpPost("users/{userId:guid}/hosted-runner-test-access")]
    public async Task<IActionResult> SetHostedRunnerTestAccess(Guid userId, [FromBody] SetHostedRunnerTestAccessRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user == null) return NotFound("User not found.");

        user.CanAccessHostedRunnerTestPlans = req.Enabled;
        await _db.SaveChangesAsync();

        await _auditEventService.WriteAsync(new AuditEventWriteRequest
        {
            EventType = AuditEventTypes.HostedRunnerTestPlanAccessChanged,
            Action = AuditActions.Changed,
            EntityType = AuditEntityTypes.User,
            EntityId = user.Id,
            TargetUserId = user.Id,
            Message = $"Hosted runner test-plan access for user '{user.Email ?? user.Id.ToString()}' was set to {req.Enabled}",
            Metadata = new
            {
                user.Email,
                req.Enabled
            }
        });

        return NoContent();
    }

    #endregion

    private static SubscriptionPlanResponse ToResponse(SubscriptionPlan p) => new()
    {
        Id                     = p.Id,
        Name                   = p.Name,
        Description            = p.Description,
        PriceUsd               = p.PriceUsd,
        StripeProductId        = p.StripeProductId,
        StripePriceId          = p.StripePriceId,
        IsDefault              = p.IsDefault,
        IsActive               = p.IsActive,
        Capabilities           = PlanCapabilitiesSerializer.Deserialize(p.CapabilitiesJson),
        SortOrder              = p.SortOrder,
        CreatedAt              = p.CreatedAt,
        UpdatedAt              = p.UpdatedAt
    };
}

// ── DTOs ────────────────────────────────────────────────────────────────────

public record SubscriptionPlanResponse
{
    public Guid    Id                     { get; init; }
    public string  Name                   { get; init; } = string.Empty;
    public string? Description            { get; init; }
    public decimal PriceUsd               { get; init; }
    public string? StripeProductId        { get; init; }
    public string? StripePriceId          { get; init; }
    public bool    IsDefault              { get; init; }
    public bool    IsActive               { get; init; }
    public PlanCapabilities Capabilities  { get; init; } = PlanCapabilities.Default;
    public int     SortOrder              { get; init; }
    public DateTime  CreatedAt            { get; init; }
    public DateTime? UpdatedAt            { get; init; }
}

public record CreateSubscriptionPlanRequest
{
    public string  Name                   { get; init; } = string.Empty;
    public string? Description            { get; init; }
    public decimal PriceUsd               { get; init; } = 0m;
    public string? StripeProductId        { get; init; }
    public string? StripePriceId          { get; init; }
    public bool    IsDefault              { get; init; } = false;
    public bool    IsActive               { get; init; } = true;
    public PlanCapabilities Capabilities  { get; init; } = PlanCapabilities.Default;
    public int     SortOrder              { get; init; } = 0;
}

public record UpdateSubscriptionPlanRequest
{
    public string?  Name                   { get; init; }
    public string?  Description            { get; init; }
    public decimal? PriceUsd               { get; init; }
    public string?  StripeProductId        { get; init; }
    public string?  StripePriceId          { get; init; }
    public bool?    IsDefault              { get; init; }
    public bool?    IsActive               { get; init; }
    public PlanCapabilities? Capabilities  { get; init; }
    public int?     SortOrder              { get; init; }
}

public record UserPlanResponse
{
    public Guid    UserId              { get; init; }
    public string  Email               { get; init; } = string.Empty;
    public string  Name                { get; init; } = string.Empty;
    public Guid?   SubscriptionPlanId  { get; init; }
    public string? PlanName            { get; init; }
    public bool    CanAccessHostedRunnerTestPlans { get; init; }
}

public record AssignPlanRequest
{
    public Guid? PlanId { get; init; }
}

public record SetHostedRunnerTestAccessRequest
{
    public bool Enabled { get; init; }
}
