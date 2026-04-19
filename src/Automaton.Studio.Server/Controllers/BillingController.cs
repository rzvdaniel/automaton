using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Studio.Server.Controllers;

[ApiController]
[Route("api/billing")]
[Authorize]
public class BillingController : BaseController
{
    private readonly StripeService _stripe;
    private readonly ApplicationDbContext _db;
    private readonly ConfigurationService _config;

    public BillingController(StripeService stripe, ApplicationDbContext db, ConfigurationService config)
    {
        _stripe = stripe;
        _db = db;
        _config = config;
    }

    /// <summary>
    /// Creates a Stripe Checkout session for upgrading to the given plan.
    /// Returns the Stripe-hosted checkout URL to redirect the user to.
    /// </summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckout([FromQuery] Guid planId)
    {
        var userId = GetUserId();

        var baseUrl = _config.WebsiteUrl.TrimEnd('/');
        var successUrl = $"{baseUrl}/billing/success?session_id={{CHECKOUT_SESSION_ID}}";
        var successUrlDirect = $"{baseUrl}/billing/success";
        var cancelUrl  = $"{baseUrl}/pricing";

        try
        {
            var url = await _stripe.CreateCheckoutSessionAsync(userId, planId, successUrl, cancelUrl);
            // If the user already had a subscription, the service upgrades it in place and
            // returns the plain success URL (no Stripe session involved).
            var checkoutUrl = url.Contains("{CHECKOUT_SESSION_ID}") ? successUrlDirect : url;
            return Ok(new { checkoutUrl });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Creates a Stripe Billing Portal session so the user can manage/cancel their subscription.
    /// Returns the portal URL.
    /// </summary>
    [HttpPost("portal")]
    public async Task<IActionResult> CreatePortal()
    {
        var userId = GetUserId();

        var returnUrl = $"{_config.WebsiteUrl.TrimEnd('/')}/billing";

        try
        {
            var url = await _stripe.CreateBillingPortalSessionAsync(userId, returnUrl);
            return Ok(new { portalUrl = url });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Returns the current user's subscription status: plan name, usage, Stripe subscription state.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var userId = GetUserId();
        var now = DateTime.UtcNow;

        var allowance = await _db.UserTokenAllowances
            .AsNoTracking()
            .Include(a => a.SubscriptionPlan)
            .FirstOrDefaultAsync(a => a.UserId == userId);

        var user = await _db.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.StripeSubscriptionId })
            .FirstOrDefaultAsync(u => u.Id == userId);

        var hasByokKeys = await _db.AiApiKeys
            .AsNoTracking()
            .AnyAsync(k => k.CreatedByUserId == userId);

        var isCurrentMonth = allowance?.CurrentMonth == now.Month && allowance?.CurrentYear == now.Year;
        var currentCost = (isCurrentMonth ? allowance?.CurrentMonthCostUsd : 0m) ?? 0m;
        var limit = allowance?.MonthlyCostLimitUsd ?? 0m;
        var percent = limit > 0 ? (int)Math.Min(100, Math.Round(currentCost / limit * 100)) : 0;

        return Ok(new
        {
            planName            = allowance?.SubscriptionPlan?.Name ?? "Free",
            priceUsd            = allowance?.SubscriptionPlan?.PriceUsd ?? 0m,
            currentMonthCostUsd = currentCost,
            monthlyCostLimitUsd = limit,
            percentUsed         = percent,
            hasActiveSubscription = !string.IsNullOrEmpty(user?.StripeSubscriptionId),
            canCreateProjects   = allowance?.SubscriptionPlan?.CanCreateProjects ?? false,
            canCreateOrganizations = allowance?.SubscriptionPlan?.CanCreateOrganizations ?? false,
            hasByokKeys,
        });
    }
}
