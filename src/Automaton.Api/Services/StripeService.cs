using Automaton.Api.Data;
using Automaton.Api.Entities;
using Automaton.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Automaton.Api.Services;

public class StripeOptions
{
    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
}

public class StripeService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly StripeWebhookService _webhookService;
    private readonly ILogger<StripeService> _logger;

    public StripeService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IOptionsSnapshot<StripeOptions> options,
        StripeWebhookService webhookService,
        ILogger<StripeService> logger)
    {
        _db = db;
        _userManager = userManager;
        _webhookService = webhookService;
        _logger = logger;
        StripeConfiguration.ApiKey = options.Value.SecretKey;
    }

    // ── Checkout ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a Stripe Checkout session for the given plan.
    /// Returns the Checkout URL to redirect the user to.
    /// </summary>
    public async Task<string> CreateCheckoutSessionAsync(Guid userId, Guid planId, string successUrl, string cancelUrl)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found");

        var plan = await _db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId)
            ?? throw new InvalidOperationException("Plan not found");

        if (!plan.IsActive)
            throw new InvalidOperationException($"Plan '{plan.Name}' is not active.");

        var capabilities = PlanCapabilitiesSerializer.Deserialize(plan.CapabilitiesJson);
        if (!HostedRunnerPlanVisibility.IsVisibleInCatalog(capabilities, user.CanAccessHostedRunnerTestPlans))
            throw new InvalidOperationException($"Plan '{plan.Name}' is not available for this user.");

        if (string.IsNullOrEmpty(plan.StripePriceId))
            throw new InvalidOperationException($"Plan '{plan.Name}' has no Stripe Price ID configured.");

        // If the user already has an active subscription, upgrade/downgrade it in place
        // instead of creating a second subscription.
        if (!string.IsNullOrEmpty(user.StripeSubscriptionId))
        {
            await UpgradeSubscriptionAsync(user, plan);
            return successUrl.Split('?')[0]; // redirect straight to success, no Stripe checkout needed
        }

        // Ensure customer exists in Stripe
        var customerId = await EnsureStripeCustomerAsync(user);

        var sessionService = new SessionService();
        var session = await sessionService.CreateAsync(new SessionCreateOptions
        {
            Customer = customerId,
            Mode = "subscription",
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = plan.StripePriceId,
                    Quantity = 1,
                }
            ],
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = userId.ToString(),
                ["planId"] = planId.ToString(),
            },
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    ["userId"] = userId.ToString(),
                    ["planId"] = planId.ToString(),
                }
            }
        });

        return session.Url;
    }

    /// <summary>
    /// Upgrades or downgrades an existing subscription to a new plan in place.
    /// Stripe prorates the charge automatically.
    /// </summary>
    private async Task UpgradeSubscriptionAsync(ApplicationUser user, SubscriptionPlan newPlan)
    {
        var subscriptionService = new SubscriptionService();
        var subscription = await subscriptionService.GetAsync(user.StripeSubscriptionId,
            new SubscriptionGetOptions { Expand = ["items"] });

        var existingItemId = subscription.Items?.Data?.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("Subscription has no line items.");

        await subscriptionService.UpdateAsync(user.StripeSubscriptionId, new SubscriptionUpdateOptions
        {
            Items =
            [
                new SubscriptionItemOptions
                {
                    Id = existingItemId,
                    Price = newPlan.StripePriceId,
                    Quantity = 1,
                }
            ],
            ProrationBehavior = "create_prorations",
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = user.Id.ToString(),
                ["planId"] = newPlan.Id.ToString(),
            }
        });

        // Do NOT assign the plan here — wait for customer.subscription.updated webhook
        // which fires only after Stripe confirms the subscription is active (payment succeeded).
        _logger.LogInformation("Subscription change requested — user {UserId} → plan {PlanName}. Awaiting webhook.", user.Id, newPlan.Name);
    }

    // ── Billing Portal ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a Stripe Billing Portal session so the user can manage/cancel their subscription.
    /// </summary>
    public async Task<string> CreateBillingPortalSessionAsync(Guid userId, string returnUrl)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found");

        if (string.IsNullOrEmpty(user.StripeCustomerId))
            throw new InvalidOperationException("No Stripe customer found for this user.");

        var portalService = new Stripe.BillingPortal.SessionService();
        var session = await portalService.CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = user.StripeCustomerId,
            ReturnUrl = returnUrl,
        });

        return session.Url;
    }

    // ── Webhook ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates and processes a Stripe webhook event.
    /// </summary>
    public async Task HandleWebhookAsync(string payload, string stripeSignature)
    {
        await _webhookService.HandleWebhookAsync(payload, stripeSignature);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<string> EnsureStripeCustomerAsync(ApplicationUser user)
    {
        if (!string.IsNullOrEmpty(user.StripeCustomerId))
            return user.StripeCustomerId;

        var customerService = new CustomerService();
        var customer = await customerService.CreateAsync(new CustomerCreateOptions
        {
            Email = user.Email,
            Name = user.Name ?? user.Email,
            Metadata = new Dictionary<string, string> { ["userId"] = user.Id.ToString() }
        });

        user.StripeCustomerId = customer.Id;
        await _userManager.UpdateAsync(user);
        return customer.Id;
    }
}
