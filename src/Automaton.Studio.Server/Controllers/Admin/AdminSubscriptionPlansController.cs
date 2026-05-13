using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Models;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers.Admin;

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
    private readonly ILogger<AdminSubscriptionPlansController> _logger;

    public AdminSubscriptionPlansController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        HostedRunnerProvisioningService hostedRunnerProvisioningService,
        ILogger<AdminSubscriptionPlansController> logger)
    {
        _db = db;
        _userManager = userManager;
        _hostedRunnerProvisioningService = hostedRunnerProvisioningService;
        _logger = logger;
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
    public async Task<ActionResult<SubscriptionPlanResponse>> CreatePlan([FromBody] CreateSubscriptionPlanRequest req)
    {
        var isActive = req.IsActive;
        var isDefault = isActive && req.IsDefault;

        // Only one default plan at a time
        if (isDefault)
            await _db.SubscriptionPlans
                .Where(p => p.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDefault, false));

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
        await _db.SaveChangesAsync();
        _logger.LogInformation("Subscription plan '{Name}' created (id={Id})", plan.Name, plan.Id);

        return CreatedAtAction(nameof(GetPlan), new { id = plan.Id }, ToResponse(plan));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SubscriptionPlanResponse>> UpdatePlan(Guid id, [FromBody] UpdateSubscriptionPlanRequest req)
    {
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
        if (plan == null) return NotFound();

        var newIsActive = req.IsActive ?? plan.IsActive;
        var newIsDefault = (req.IsDefault ?? plan.IsDefault) && newIsActive;

        if (newIsDefault && !plan.IsDefault)
            await _db.SubscriptionPlans
                .Where(p => p.IsDefault && p.Id != id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDefault, false));

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

        await _db.SaveChangesAsync();
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
    public async Task<IActionResult> AssignPlan(Guid userId, [FromBody] AssignPlanRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user == null) return NotFound("User not found.");

        if (req.PlanId.HasValue)
        {
            var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == req.PlanId.Value);
            if (plan == null) return NotFound("Subscription plan not found.");
        }

        user.SubscriptionPlanId = req.PlanId;
        await _db.SaveChangesAsync();

        if (req.PlanId.HasValue)
        {
            await _hostedRunnerProvisioningService.EnsureHostedRunnerForUserPlanAsync(userId, req.PlanId.Value);
        }
        else
        {
            await _hostedRunnerProvisioningService.DisableHostedRunnersForUserAsync(userId, "Admin removed subscription plan.");
        }

        _logger.LogInformation(
            "Plan {PlanId} assigned to user {UserId}",
            req.PlanId, userId);

        return NoContent();
    }

    [HttpPost("users/{userId:guid}/hosted-runner-test-access")]
    public async Task<IActionResult> SetHostedRunnerTestAccess(Guid userId, [FromBody] SetHostedRunnerTestAccessRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
        if (user == null) return NotFound("User not found.");

        user.CanAccessHostedRunnerTestPlans = req.Enabled;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Hosted runner test-plan access for user {UserId} set to {Enabled}",
            userId,
            req.Enabled);

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
