using Automaton.Api.Data;
using Automaton.Api.Models;
using Automaton.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Automaton.Api.Controllers;

[ApiController]
[Route("api/billing")]
[Authorize]
public class BillingController : BaseController
{
    private readonly StripeService _stripe;
    private readonly ApplicationDbContext _db;
    private readonly ConfigurationService _config;
    private readonly ConfirmedEmailGuardService _confirmedEmailGuardService;

    public BillingController(
        StripeService stripe,
        ApplicationDbContext db,
        ConfigurationService config,
        ConfirmedEmailGuardService confirmedEmailGuardService)
    {
        _stripe = stripe;
        _db = db;
        _config = config;
        _confirmedEmailGuardService = confirmedEmailGuardService;
    }

    /// <summary>
    /// Creates a Stripe Checkout session for upgrading to the given plan.
    /// Returns the Stripe-hosted checkout URL to redirect the user to.
    /// </summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckout([FromQuery] Guid planId)
    {
        var userId = GetUserId();
        if (!await _confirmedEmailGuardService.IsConfirmedAsync(userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

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
        if (!await _confirmedEmailGuardService.IsConfirmedAsync(userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ConfirmedEmailGuardService.SensitiveActionMessage });
        }

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
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.SubscriptionPlan)
            .Select(u => new
            {
                u.Id,
                u.StripeSubscriptionId,
                PlanName = u.SubscriptionPlan != null ? u.SubscriptionPlan.Name : null,
                PlanPriceUsd = u.SubscriptionPlan != null ? u.SubscriptionPlan.PriceUsd : 0m
            })
            .FirstOrDefaultAsync(u => u.Id == userId);

        return Ok(new
        {
            planName            = user?.PlanName ?? "Free",
            priceUsd            = user?.PlanPriceUsd ?? 0m,
            hasActiveSubscription = !string.IsNullOrEmpty(user?.StripeSubscriptionId)
        });
    }
}
