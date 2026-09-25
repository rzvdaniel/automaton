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
    private readonly AdminSubscriptionPlanOperationsService _planOperations;

    public AdminSubscriptionPlansController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        AdminSubscriptionPlanOperationsService planOperations)
    {
        _db = db;
        _userManager = userManager;
        _planOperations = planOperations;
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
        var (plan, capabilityError) = await _planOperations.CreateAsync(req, cancellationToken);
        if (capabilityError is not null) return BadRequest(new { message = capabilityError });

        return CreatedAtAction(nameof(GetPlan), new { id = plan!.Id }, ToResponse(plan));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SubscriptionPlanResponse>> UpdatePlan(Guid id, [FromBody] UpdateSubscriptionPlanRequest req, CancellationToken cancellationToken)
    {
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan == null) return NotFound();

        if (req.Capabilities != null)
        {
            var capabilityError = await _planOperations.ValidateCapabilitiesAsync(req.Capabilities, cancellationToken);
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

        SubscriptionPlanUpdateApplier.Apply(plan, req, newIsDefault);

        await _db.SaveChangesAsync(cancellationToken);
        if (transaction != null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Ok(ToResponse(plan));
    }

    #endregion

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
        var result = await _planOperations.AssignAsync(userId, req.PlanId, cancellationToken);
        return result.Failure switch
        {
            PlanAssignmentFailure.UserNotFound => NotFound("User not found."),
            PlanAssignmentFailure.PlanNotFound => NotFound("Subscription plan not found."),
            PlanAssignmentFailure.InvalidCapability => BadRequest(new { message = result.Error }),
            _ => NoContent()
        };
    }

    [HttpPost("users/{userId:guid}/hosted-runner-test-access")]
    public async Task<IActionResult> SetHostedRunnerTestAccess(Guid userId, [FromBody] SetHostedRunnerTestAccessRequest req)
    {
        return await _planOperations.SetHostedRunnerTestAccessAsync(userId, req.Enabled)
            ? NoContent()
            : NotFound("User not found.");
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
