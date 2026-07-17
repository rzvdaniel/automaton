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
    private readonly StripeOptions _options;
    private readonly HostedRunnerProvisioningService _hostedRunnerProvisioningService;
    private readonly ILogger<StripeService> _logger;

    public StripeService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IOptionsSnapshot<StripeOptions> options,
        HostedRunnerProvisioningService hostedRunnerProvisioningService,
        ILogger<StripeService> logger)
    {
        _db = db;
        _userManager = userManager;
        _options = options.Value;
        _hostedRunnerProvisioningService = hostedRunnerProvisioningService;
        _logger = logger;
        StripeConfiguration.ApiKey = _options.SecretKey;
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
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, stripeSignature, _options.WebhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Stripe webhook signature validation failed: {Message}", ex.Message);
            throw;
        }

        _logger.LogInformation("Stripe webhook received: {EventType}", stripeEvent.Type);

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                await HandleCheckoutCompletedAsync((Session)stripeEvent.Data.Object);
                break;

            case EventTypes.InvoicePaymentSucceeded:
                await HandlePaymentSucceededAsync((Invoice)stripeEvent.Data.Object);
                break;

            case EventTypes.InvoicePaymentFailed:
                await HandlePaymentFailedAsync((Invoice)stripeEvent.Data.Object);
                break;

            case EventTypes.CustomerSubscriptionDeleted:
                await HandleSubscriptionDeletedAsync((Subscription)stripeEvent.Data.Object);
                break;

            case EventTypes.CustomerSubscriptionUpdated:
                await HandleSubscriptionUpdatedAsync((Subscription)stripeEvent.Data.Object);
                break;

            default:
                _logger.LogDebug("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                break;
        }
    }

    // ── Webhook handlers ──────────────────────────────────────────────────────

    private async Task HandleCheckoutCompletedAsync(Session session)
    {
        if (!session.Metadata.TryGetValue("userId", out var userIdStr) ||
            !session.Metadata.TryGetValue("planId", out var planIdStr))
        {
            _logger.LogWarning("Checkout session {SessionId} missing metadata", session.Id);
            return;
        }

        if (!Guid.TryParse(userIdStr, out var userId) || !Guid.TryParse(planIdStr, out var planId))
            return;

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return;

        // session.SubscriptionId can be null when the event fires — expand to get it
        var subscriptionId = session.SubscriptionId;
        if (string.IsNullOrEmpty(subscriptionId))
        {
            var expanded = await new SessionService().GetAsync(session.Id,
                new SessionGetOptions { Expand = ["subscription"] });
            subscriptionId = expanded.SubscriptionId ?? expanded.Subscription?.Id;
        }

        user.StripeCustomerId    = session.CustomerId;
        user.StripeSubscriptionId = subscriptionId;
        await _userManager.UpdateAsync(user);

        await AssignPlanAsync(userId, planId);
        _logger.LogInformation("Checkout completed — user {UserId} assigned to plan {PlanId}", userId, planId);
    }

    private async Task HandlePaymentSucceededAsync(Invoice invoice)
    {
        // Payment succeeded — subscription is active. Nothing to do (plan already assigned at checkout).
        _logger.LogInformation("Payment succeeded for customer {CustomerId}", invoice.CustomerId);
    }

    private async Task HandlePaymentFailedAsync(Invoice invoice)
    {
        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.StripeCustomerId == invoice.CustomerId);
        if (user == null) return;

        await DowngradeToFreeAsync(user.Id);
        _logger.LogWarning("Payment failed — user {UserId} downgraded to Free", user.Id);
    }

    private async Task HandleSubscriptionDeletedAsync(Subscription subscription)
    {
        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.StripeSubscriptionId == subscription.Id);
        if (user == null) return;

        user.StripeSubscriptionId = null;
        await _userManager.UpdateAsync(user);
        await DowngradeToFreeAsync(user.Id);
        _logger.LogInformation("Subscription cancelled — user {UserId} downgraded to Free", user.Id);
    }

    private async Task HandleSubscriptionUpdatedAsync(Subscription subscription)
    {
        // Only assign the plan when Stripe confirms the subscription is active.
        // "active" means the latest invoice was paid. This prevents granting higher-tier
        // access before payment has been collected.
        if (subscription.Status != "active")
        {
            _logger.LogInformation(
                "Subscription {SubId} updated but status is '{Status}' — skipping plan assignment",
                subscription.Id, subscription.Status);
            return;
        }

        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.StripeSubscriptionId == subscription.Id);
        if (user == null) return;

        var newPriceId = subscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
        if (string.IsNullOrEmpty(newPriceId)) return;

        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.StripePriceId == newPriceId && p.IsActive);
        if (plan == null) return;

        await AssignPlanAsync(user.Id, plan.Id);
        _logger.LogInformation("Subscription updated — user {UserId} moved to plan {PlanName}", user.Id, plan.Name);
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

    private async Task AssignPlanAsync(Guid userId, Guid planId)
    {
        var plan = await _db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId && p.IsActive);
        if (plan == null) return;

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return;

        user.SubscriptionPlanId = plan.Id;
        await _userManager.UpdateAsync(user);

        try
        {
            await _hostedRunnerProvisioningService.EnsureHostedRunnerAsync(userId);
        }
        catch (Exception ex)
        {
            // Stripe webhooks must stay idempotent and retryable; provisioning failures are tracked separately.
            _logger.LogError(ex, "Hosted runner provisioning failed after assigning plan {PlanId} to user {UserId}", plan.Id, userId);
        }
    }

    private async Task DowngradeToFreeAsync(Guid userId)
    {
        var freePlan = await _db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.IsDefault && p.IsActive);
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null) return;

        user.SubscriptionPlanId = freePlan?.Id;
        await _userManager.UpdateAsync(user);

        await _hostedRunnerProvisioningService.DisableHostedRunnersForUserAsync(
            userId,
            "Subscription was downgraded or cancelled.");
    }
}
