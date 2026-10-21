using Automaton.Api.Data;
using Automaton.Api.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Automaton.Api.Services;

public sealed class StripeWebhookService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptionsSnapshot<StripeOptions> options,
    ILogger<StripeWebhookService> logger)
{
    private readonly string webhookSecret = options.Value.WebhookSecret;

    /// <summary>
    /// Validates and processes a Stripe webhook event.
    /// </summary>
    public async Task HandleWebhookAsync(string payload, string stripeSignature)
    {
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, stripeSignature, webhookSecret);
        }
        catch (StripeException exception)
        {
            logger.LogWarning(exception, "Stripe webhook signature validation failed");
            throw;
        }

        logger.LogInformation("Stripe webhook received: {EventType}", stripeEvent.Type);

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                await HandleCheckoutCompletedAsync((Session)stripeEvent.Data.Object);
                break;
            case EventTypes.InvoicePaymentSucceeded:
                HandlePaymentSucceeded((Invoice)stripeEvent.Data.Object);
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
                logger.LogDebug("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                break;
        }
    }

    private async Task HandleCheckoutCompletedAsync(Session session)
    {
        if (!session.Metadata.TryGetValue("userId", out var userIdText) ||
            !session.Metadata.TryGetValue("planId", out var planIdText))
        {
            logger.LogWarning("Checkout session {SessionId} missing metadata", session.Id);
            return;
        }

        if (!Guid.TryParse(userIdText, out var userId) || !Guid.TryParse(planIdText, out var planId))
        {
            return;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return;
        }

        var subscriptionId = session.SubscriptionId;
        if (string.IsNullOrEmpty(subscriptionId))
        {
            var expandedSession = await new SessionService().GetAsync(
                session.Id,
                new SessionGetOptions { Expand = ["subscription"] });
            subscriptionId = expandedSession.SubscriptionId ?? expandedSession.Subscription?.Id;
        }

        user.StripeCustomerId = session.CustomerId;
        user.StripeSubscriptionId = subscriptionId;
        await userManager.UpdateAsync(user);
        await AssignPlanAsync(userId, planId);
        logger.LogInformation("Checkout completed — user {UserId} assigned to plan {PlanId}", userId, planId);
    }

    private void HandlePaymentSucceeded(Invoice invoice)
    {
        // Payment succeeded — subscription is active. Nothing to do (plan already assigned at checkout).
        logger.LogInformation("Payment succeeded for customer {CustomerId}", invoice.CustomerId);
    }

    private async Task HandlePaymentFailedAsync(Invoice invoice)
    {
        var user = await userManager.Users.FirstOrDefaultAsync(candidate => candidate.StripeCustomerId == invoice.CustomerId);
        if (user is null)
        {
            return;
        }

        await DowngradeToFreeAsync(user.Id);
        logger.LogWarning("Payment failed — user {UserId} downgraded to Free", user.Id);
    }

    private async Task HandleSubscriptionDeletedAsync(Subscription subscription)
    {
        var user = await userManager.Users.FirstOrDefaultAsync(candidate => candidate.StripeSubscriptionId == subscription.Id);
        if (user is null)
        {
            return;
        }

        user.StripeSubscriptionId = null;
        await userManager.UpdateAsync(user);
        await DowngradeToFreeAsync(user.Id);
        logger.LogInformation("Subscription cancelled — user {UserId} downgraded to Free", user.Id);
    }

    private async Task HandleSubscriptionUpdatedAsync(Subscription subscription)
    {
        if (subscription.Status != "active")
        {
            logger.LogInformation(
                "Subscription {SubId} updated but status is '{Status}' — skipping plan assignment",
                subscription.Id,
                subscription.Status);
            return;
        }

        var user = await userManager.Users.FirstOrDefaultAsync(candidate => candidate.StripeSubscriptionId == subscription.Id);
        if (user is null)
        {
            return;
        }

        var newPriceId = subscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
        if (string.IsNullOrEmpty(newPriceId))
        {
            return;
        }

        var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(candidate => candidate.StripePriceId == newPriceId && candidate.IsActive);
        if (plan is null)
        {
            return;
        }

        await AssignPlanAsync(user.Id, plan.Id);
        logger.LogInformation("Subscription updated — user {UserId} moved to plan {PlanName}", user.Id, plan.Name);
    }

    private async Task AssignPlanAsync(Guid userId, Guid planId)
    {
        var plan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == planId && candidate.IsActive);
        if (plan is null)
        {
            return;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return;
        }

        user.SubscriptionPlanId = plan.Id;
        await userManager.UpdateAsync(user);
    }

    private async Task DowngradeToFreeAsync(Guid userId)
    {
        var freePlan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(plan => plan.IsDefault && plan.IsActive);
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return;
        }

        user.SubscriptionPlanId = freePlan?.Id;
        await userManager.UpdateAsync(user);
    }
}
