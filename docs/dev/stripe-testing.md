# Testing Stripe Integration in Development

Stripe provides a full **test mode** that lets you exercise the entire payment flow — checkout, webhooks, billing portal, cancellations — without real money.

## Test Mode Keys

Use test keys (already set in `appsettings.Development.json`):

| Key | Prefix |
|-----|--------|
| Secret key | `sk_test_...` |
| Publishable key | `pk_test_...` |
| Webhook secret | `whsec_...` (from `stripe listen`) |

Test transactions are completely free and isolated from production.

## Test Credit Cards

| Card number | Behaviour |
|-------------|-----------|
| `4242 4242 4242 4242` | Successful payment |
| `4000 0000 0000 9995` | Card declined (tests `invoice.payment_failed`) |
| `4000 0025 0000 3155` | Requires 3D Secure authentication |

Use any future expiry date and any 3-digit CVC.  
Full list: https://stripe.com/docs/testing

## Webhook Forwarding (Local Development)

Stripe needs a public URL to POST webhook events. The easiest approach is the **Stripe CLI**:

```bash
# Install once (https://stripe.com/docs/stripe-cli)
stripe login
stripe listen --forward-to https://localhost:7091/api/webhooks/stripe
```

The CLI prints a `whsec_...` secret — copy it into `appsettings.Development.json` under `Stripe.WebhookSecret`.

Alternative: expose localhost with **ngrok** and register that URL in the Stripe Dashboard.

## Config (`appsettings.Development.json`)

```json
"Stripe": {
  "SecretKey": "sk_test_...",
  "PublishableKey": "pk_test_...",
  "WebhookSecret": "whsec_...",
  "SuccessUrl": "http://localhost:5173/billing/success",
  "CancelUrl": "http://localhost:5173/billing/cancel"
}
```

## What You Can Test End-to-End

| Scenario | How to trigger | Webhook fired |
|----------|---------------|---------------|
| Successful new subscription | Checkout with `4242...` card | `checkout.session.completed` → plan activated |
| Declined payment on new subscription | Checkout with `4000...9995` card | `invoice.payment_failed` |
| Upgrade existing subscription (success) | Pricing page → Upgrade (no checkout) → `4242...` card on file | `customer.subscription.updated` (status=active) → plan upgraded |
| Upgrade existing subscription (declined) | Change card to `4000 0000 0000 0341` → Pricing page → Upgrade | `customer.subscription.updated` (status=past_due) → plan NOT upgraded |
| Cancel subscription | Stripe billing portal → Cancel | `customer.subscription.deleted` → downgrade to Free |
| Renewal payment | Advance test clock in Stripe Dashboard | `invoice.payment_succeeded` |

## Testing Upgrade with Failed Payment

1. In Stripe Dashboard (test mode) → Customers → your user → Payment methods
2. Add test card `4000 0000 0000 0341` (always declines) and set as default
3. Go to `/pricing` in Automaton → click Upgrade to a higher plan
4. Watch `stripe listen` — `customer.subscription.updated` fires with `status=past_due`
5. Server logs: `skipping plan assignment` — user stays on their current plan
6. Clean up: switch card back to `4242 4242 4242 4242`

## Important: `whsec_` Secret Changes Every Session

`stripe listen` generates a **new signing secret every time it starts**. After restarting the CLI you must update `appsettings.Development.json`:

```json
"Stripe": {
  "WebhookSecret": "whsec_<new_value_from_stripe_listen>"
}
```

Then press **Ctrl+R** in the `dotnet watch` terminal to force a restart (config is cached at startup).

Or use .NET user secrets to avoid editing files:
```bash
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..."
```

## Development Workflow

1. Start backend: `dotnet watch` in `src/Automaton.Api`
2. Start frontend: `npm run dev` in `src/automaton-studio-vue`
3. Start Stripe CLI: `stripe listen --forward-to https://localhost:7091/api/webhooks/stripe`
4. In the Admin panel, set `StripeProductId` and `StripePriceId` on each paid plan
5. Navigate to `/pricing`, click Upgrade, complete checkout with a test card
6. Verify plan is activated in Admin → Subscription Plans → Users
7. Navigate to `/billing`, click "Manage Subscription" to test the billing portal

## Stripe Dashboard Test Mode

Switch to test mode in the Stripe Dashboard (toggle top-left) to:
- See all test transactions, customers, subscriptions
- Advance the billing clock to trigger renewal webhooks
- Inspect webhook delivery logs and retry failed events
- Create products and prices to copy IDs into the admin panel
