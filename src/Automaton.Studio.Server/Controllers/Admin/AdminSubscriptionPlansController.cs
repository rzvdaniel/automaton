using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Automaton.Studio.Server.Services.Interfaces;
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
    private readonly ITokenBudgetService _tokenBudgetService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AdminSubscriptionPlansController> _logger;

    public AdminSubscriptionPlansController(
        ApplicationDbContext db,
        ITokenBudgetService tokenBudgetService,
        UserManager<ApplicationUser> userManager,
        ILogger<AdminSubscriptionPlansController> logger)
    {
        _db = db;
        _tokenBudgetService = tokenBudgetService;
        _userManager = userManager;
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
        // Only one default plan at a time
        if (req.IsDefault)
            await _db.SubscriptionPlans
                .Where(p => p.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDefault, false));

        var plan = new SubscriptionPlan
        {
            Id                 = Guid.NewGuid(),
            Name               = req.Name,
            Description        = req.Description,
            MonthlyCostLimitUsd = req.MonthlyCostLimitUsd,
            IsDefault          = req.IsDefault,
            SortOrder          = req.SortOrder,
            CreatedAt          = DateTime.UtcNow
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

        if (req.IsDefault == true && !plan.IsDefault)
            await _db.SubscriptionPlans
                .Where(p => p.IsDefault && p.Id != id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDefault, false));

        if (req.Name        != null) plan.Name               = req.Name;
        if (req.Description != null) plan.Description        = req.Description;
        if (req.MonthlyCostLimitUsd.HasValue) plan.MonthlyCostLimitUsd = req.MonthlyCostLimitUsd.Value;
        if (req.IsDefault   .HasValue) plan.IsDefault        = req.IsDefault.Value;
        if (req.SortOrder   .HasValue) plan.SortOrder        = req.SortOrder.Value;
        plan.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(ToResponse(plan));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeletePlan(Guid id)
    {
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
        if (plan == null) return NotFound();

        // Detach users from this plan before deleting
        await _db.UserTokenAllowances
            .Where(a => a.SubscriptionPlanId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SubscriptionPlanId, (Guid?)null));

        _db.SubscriptionPlans.Remove(plan);
        await _db.SaveChangesAsync();
        return NoContent();
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
            .OrderBy(u => u.Email)
            .ToListAsync();

        var allowances = await _db.UserTokenAllowances
            .AsNoTracking()
            .Include(a => a.SubscriptionPlan)
            .ToListAsync();

        var allowanceMap = allowances.ToDictionary(a => a.UserId);
        var now = DateTime.UtcNow;

        var result = users.Select(u =>
        {
            allowanceMap.TryGetValue(u.Id, out var allowance);
            var isCurrentMonth = allowance?.CurrentMonth == now.Month && allowance?.CurrentYear == now.Year;

            return new UserPlanResponse
            {
                UserId              = u.Id,
                Email               = u.Email ?? string.Empty,
                Name                = u.Name ?? u.Email ?? string.Empty,
                SubscriptionPlanId  = allowance?.SubscriptionPlanId,
                PlanName            = allowance?.SubscriptionPlan?.Name,
                MonthlyCostLimitUsd = allowance?.MonthlyCostLimitUsd ?? 10m,
                CurrentMonthCostUsd = isCurrentMonth ? (allowance?.CurrentMonthCostUsd ?? 0m) : 0m,
                OverageAction       = allowance?.OverageAction ?? OverageActions.Downgrade
            };
        });

        return Ok(result);
    }

    /// <summary>
    /// Assign a subscription plan to a user. Updates their MonthlyCostLimitUsd immediately.
    /// Pass planId = null to remove the plan and reset to default ($10).
    /// </summary>
    [HttpPost("users/{userId:guid}/assign")]
    public async Task<IActionResult> AssignPlan(Guid userId, [FromBody] AssignPlanRequest req)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return NotFound("User not found.");

        decimal limitUsd;

        if (req.PlanId.HasValue)
        {
            var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == req.PlanId.Value);
            if (plan == null) return NotFound("Subscription plan not found.");
            limitUsd = plan.MonthlyCostLimitUsd;
        }
        else
        {
            limitUsd = 10.00m; // default free tier
        }

        await _tokenBudgetService.SetAllowanceAsync(userId, limitUsd, OverageActions.Downgrade);

        // Also record the plan FK directly
        var allowance = await _db.UserTokenAllowances.FirstOrDefaultAsync(a => a.UserId == userId);
        if (allowance != null)
        {
            allowance.SubscriptionPlanId = req.PlanId;
            await _db.SaveChangesAsync();
        }

        _logger.LogInformation(
            "Plan {PlanId} assigned to user {UserId} (limit=${Limit:F2}/month)",
            req.PlanId, userId, limitUsd);

        return NoContent();
    }

    #endregion

    private static SubscriptionPlanResponse ToResponse(SubscriptionPlan p) => new()
    {
        Id                  = p.Id,
        Name                = p.Name,
        Description         = p.Description,
        MonthlyCostLimitUsd = p.MonthlyCostLimitUsd,
        IsDefault           = p.IsDefault,
        SortOrder           = p.SortOrder,
        CreatedAt           = p.CreatedAt,
        UpdatedAt           = p.UpdatedAt
    };
}

// ── DTOs ────────────────────────────────────────────────────────────────────

public record SubscriptionPlanResponse
{
    public Guid    Id                  { get; init; }
    public string  Name                { get; init; } = string.Empty;
    public string? Description         { get; init; }
    public decimal MonthlyCostLimitUsd { get; init; }
    public bool    IsDefault           { get; init; }
    public int     SortOrder           { get; init; }
    public DateTime  CreatedAt         { get; init; }
    public DateTime? UpdatedAt         { get; init; }
}

public record CreateSubscriptionPlanRequest
{
    public string  Name                { get; init; } = string.Empty;
    public string? Description         { get; init; }
    public decimal MonthlyCostLimitUsd { get; init; } = 10m;
    public bool    IsDefault           { get; init; } = false;
    public int     SortOrder           { get; init; } = 0;
}

public record UpdateSubscriptionPlanRequest
{
    public string?  Name                { get; init; }
    public string?  Description         { get; init; }
    public decimal? MonthlyCostLimitUsd { get; init; }
    public bool?    IsDefault           { get; init; }
    public int?     SortOrder           { get; init; }
}

public record UserPlanResponse
{
    public Guid    UserId              { get; init; }
    public string  Email               { get; init; } = string.Empty;
    public string  Name                { get; init; } = string.Empty;
    public Guid?   SubscriptionPlanId  { get; init; }
    public string? PlanName            { get; init; }
    public decimal MonthlyCostLimitUsd { get; init; }
    public decimal CurrentMonthCostUsd { get; init; }
    public string  OverageAction       { get; init; } = OverageActions.Downgrade;
}

public record AssignPlanRequest
{
    public Guid? PlanId { get; init; }
}
