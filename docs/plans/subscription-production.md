# Subscription System — Full Production Plan

## What We Have Today

- ✅ `SubscriptionPlan` entity with `CanCreateProjects`, `CanCreateOrganizations`, `MonthlyCostLimitUsd`
- ✅ `UserTokenAllowance` with FK to `SubscriptionPlan`
- ✅ AI spend enforced: downgrade model when limit exceeded
- ✅ Admin UI: create/edit plans, assign plans to users manually
- ✅ `TokenBudgetService.SetAllowanceAsync` — updates user limit when plan changes

## What's Missing

---

## Phase 1 — Feature Gating (no payment needed)

Gate `CanCreateProjects` and `CanCreateOrganizations` at the API level.

**Files:**
- `Services/Interfaces/ISubscriptionGatewayService.cs` — `EnsureCanCreateProjectAsync(userId)`, `EnsureCanCreateOrganizationAsync(userId)`
- `Services/SubscriptionGatewayService.cs` — loads user plan via `UserTokenAllowances → SubscriptionPlan`; falls back to `IsDefault` plan if user has no allowance row
- `Exceptions/PlanLimitException.cs` — carries `FeatureName`, `RequiredPlanName`; middleware converts to HTTP 402
- Hook into: `ProjectsController.CreateProject`, `OrganizationsController.CreateOrganization`
- Frontend: Axios interceptor catches 402 → shows upgrade dialog pointing to `/pricing`

**Schema needed:** none — all flags are in DB already.

---

## Phase 2 — Stripe Integration (backend)

### 2a. Stripe setup
- Create Stripe account, add products + prices for Starter/Pro/Business (monthly recurring)
- Store `StripeProductId` and `StripePriceId` in `SubscriptionPlan` (new columns + migration)
- Add `StripeCustomerId` and `StripeSubscriptionId` to `ApplicationUser` (or a new `UserBilling` entity)

### 2b. New backend service: `StripeService`
```
Services/StripeService.cs
```
Methods:
- `CreateCheckoutSessionAsync(userId, planId)` → returns Stripe Checkout URL
- `CreateBillingPortalSessionAsync(userId)` → returns Stripe Portal URL (for managing/cancelling)
- `HandleWebhookAsync(payload, signature)` → processes Stripe events

### 2c. Stripe Webhook handler
New controller: `StripeWebhookController`  
Route: `POST /api/webhooks/stripe` (no auth — verified by Stripe signature)

Events to handle:
| Event | Action |
|---|---|
| `checkout.session.completed` | Assign plan to user, set `StripeSubscriptionId` |
| `invoice.payment_succeeded` | Confirm subscription active for next month |
| `invoice.payment_failed` | Downgrade to Free plan |
| `customer.subscription.deleted` | Downgrade to Free plan |
| `customer.subscription.updated` | Update plan if user upgraded/downgraded in portal |

### 2d. New API endpoints (for Vue to call)
```
POST /api/billing/checkout?planId={id}   → returns { checkoutUrl }
POST /api/billing/portal                 → returns { portalUrl }
GET  /api/billing/status                 → returns current plan + subscription status
```

**NuGet package:** `Stripe.net`

---

## Phase 3 — Pricing Page (Vue)

New route: `/pricing`  
New feature folder: `src/features/billing/`

```
billing/
├── views/
│   ├── PricingView.vue        ← public pricing page, visible when logged in
│   └── BillingStatusView.vue  ← current plan, usage, manage button
├── services/
│   └── billingService.ts      ← calls /api/billing/*
├── types/
│   └── billing.types.ts
└── routes.ts
```

### PricingView.vue
- Shows 4 plan cards: Free / Starter / Pro / Business
- Highlights current plan with "Your plan" badge
- "Upgrade" button on paid plans → calls `POST /api/billing/checkout` → redirects to Stripe Checkout
- Free plan shows "Current plan" (no button) or "Downgrade" if on paid plan
- Stripe returns user to `/billing/success?session_id=...` or `/billing/cancel`

### BillingStatusView.vue  
Route: `/billing` (accessible from user Account menu)
- Current plan name + monthly AI budget used vs limit (progress bar)
- "Manage subscription" button → calls `POST /api/billing/portal` → redirects to Stripe Portal
- Link to `/pricing` to upgrade

---

## Phase 4 — Auto-assign Free plan to new users

When a user registers, automatically create their `UserTokenAllowance` row with the `IsDefault` plan.

**Where:** `AccountService` or Identity registration handler — after user creation, call `TokenBudgetService.SetAllowanceAsync` with the default plan's limit.

---

## Phase 5 — User-facing upgrade prompts

When a user hits a feature gate (402 from API):
- Show a modal: "This feature requires the **Starter** plan"
- Button: "See plans" → navigates to `/pricing`

When a user nears their AI budget (e.g. 80%+ used):
- Show a banner in the app header: "You've used 80% of your monthly AI budget. [Upgrade]"
- Load budget status from `GET /api/billing/status` on app init

---

## Phase 6 — Production checklist

- [ ] Stripe live mode keys in production environment variables (`STRIPE_SECRET_KEY`, `STRIPE_WEBHOOK_SECRET`)
- [ ] Stripe webhook endpoint registered in Stripe dashboard → `https://yourapp.com/api/webhooks/stripe`
- [ ] `SubscriptionPlan` rows in production DB seeded with correct `StripePriceId` values (see `docs/prod/subscription-plans.md`)
- [ ] Test full flow: checkout → webhook → plan assigned → feature gate enforced
- [ ] Test failed payment → downgrade to Free
- [ ] Test cancellation → downgrade to Free at period end

---

## Implementation Order

1. **Phase 1** — Feature gating (no Stripe needed, can do now)
2. **Phase 4** — Auto-assign Free on registration (simple, no Stripe)
3. **Phase 2** — Stripe backend (needs Stripe account + test keys)
4. **Phase 3** — Pricing + billing Vue pages
5. **Phase 5** — Upgrade prompts + budget banner
6. **Phase 6** — Production deploy + go-live checklist

---

## Open Questions

- Monthly or annual billing? (Annual with discount is standard SaaS — can add later)
- Trial period? (e.g. 14-day Pro trial for new users — Stripe supports this natively)
- EU VAT handling? (Stripe Tax can handle this automatically — just enable it in Stripe dashboard)
