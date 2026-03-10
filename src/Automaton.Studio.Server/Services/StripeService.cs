using Automaton.Studio.Server.Data;
using Automaton.Studio.Server.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Automaton.Studio.Server.Services;

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
    private readonly ILogger<StripeService> _logger;

    public StripeService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IOptions<StripeOptions> options,
        ILogger<StripeService> logger)
    {
        _db = db;
        _userManager = userManager;
        _options = options.Value;
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

        if (string.IsNullOrEmpty(plan.StripePriceId))
            throw new InvalidOperationException($"Plan '{plan.Name}' has no Stripe Price ID configured.");

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
        // Handle plan changes made via Billing Portal
        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.StripeSubscriptionId == subscription.Id);
        if (user == null) return;

        var newPriceId = subscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
        if (string.IsNullOrEmpty(newPriceId)) return;

        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.StripePriceId == newPriceId);
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
        var plan = await _db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId);
        if (plan == null) return;

        var allowance = await _db.UserTokenAllowances.FirstOrDefaultAsync(a => a.UserId == userId);
        if (allowance == null)
        {
            allowance = new UserTokenAllowance { Id = Guid.NewGuid(), UserId = userId };
            _db.UserTokenAllowances.Add(allowance);
        }

        allowance.MonthlyCostLimitUsd = plan.MonthlyCostLimitUsd;
        allowance.SubscriptionPlanId = plan.Id;
        allowance.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private async Task DowngradeToFreeAsync(Guid userId)
    {
        var freePlan = await _db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.IsDefault);
        if (freePlan != null)
            await AssignPlanAsync(userId, freePlan.Id);
    }
}
