# Feature Gating Plan

## What We Have Now (data model ready)

`SubscriptionPlan` has these flags — stored in DB, exposed in admin UI, but **not yet enforced**:

| Flag | Controls |
|---|---|
| `CanCreateProjects` | Can the user create new Projects |
| `CanCreateOrganizations` | Can the user create new Organizations |
| `CanUseCustomRunners` | Can the user register custom runner nodes |
| `MaxFlows` | Max flows owned (0 = unlimited) |
| `MaxApps` | Max apps owned (0 = unlimited) |
| `MaxCustomSteps` | Max custom steps owned (0 = unlimited) |
| `MonthlyCostLimitUsd` | AI spending budget — **already enforced** (downgrade) |

---

## Suggested Plan Tiers

| | Free | Starter | Pro | Business |
|---|---|---|---|---|
| AI budget/month | $2 | $10 | $30 | $100 |
| Max flows | 5 | 20 | 0 (∞) | 0 (∞) |
| Max apps | 2 | 10 | 0 (∞) | 0 (∞) |
| Max custom steps | 3 | 10 | 0 (∞) | 0 (∞) |
| Projects | ❌ | ✅ | ✅ | ✅ |
| Organizations | ❌ | ❌ | ✅ | ✅ |
| Custom runners | ❌ | ❌ | ✅ | ✅ |
| Sort order | 0 | 1 | 2 | 3 |

---

## Implementation Plan

### Phase 1 — `ISubscriptionGateway` service (backend)

Create `src/Automaton.Api/Services/Interfaces/ISubscriptionGatewayService.cs`:

```csharp
Task<SubscriptionPlan?> GetUserPlanAsync(Guid userId);
Task EnsureCanCreateProjectAsync(Guid userId);     // throws PlanLimitException
Task EnsureCanCreateOrganizationAsync(Guid userId);
Task EnsureCanCreateRunnerAsync(Guid userId);
Task EnsureFlowLimitAsync(Guid userId);            // checks current count vs MaxFlows
Task EnsureAppLimitAsync(Guid userId);
Task EnsureStepLimitAsync(Guid userId);
```

`PlanLimitException` carries: `FeatureName`, `RequiredPlan`, `CurrentPlan` — returned as HTTP 402.

How to resolve user plan:
1. Look up `UserTokenAllowances` for the user → `SubscriptionPlanId`
2. If null: use the `IsDefault` plan
3. Load `SubscriptionPlan` flags

### Phase 2 — Enforce at controller level

Hook `ISubscriptionGatewayService` into each relevant controller action:

| Controller | Action | Check |
|---|---|---|
| `ProjectsController` | `POST /projects` | `EnsureCanCreateProjectAsync` + `EnsureFlowLimitAsync` (if project creates flows) |
| `OrganizationsController` | `POST /organizations` | `EnsureCanCreateOrganizationAsync` |
| `RunnersController` | `POST /runners` (register) | `EnsureCanCreateRunnerAsync` |
| `FlowsController` | `POST /flows` | `EnsureFlowLimitAsync` |
| `AppsController` | `POST /apps` | `EnsureAppLimitAsync` |
| `StepsController` | `POST /steps` | `EnsureStepLimitAsync` |

Pattern:
```csharp
await _subscriptionGateway.EnsureCanCreateProjectAsync(userId);
// → throws PlanLimitException(402) if not allowed
```

### Phase 3 — Global exception handler for `PlanLimitException`

In `Program.cs` / middleware: catch `PlanLimitException` → return:

```json
HTTP 402 Payment Required
{
  "code": "PLAN_LIMIT",
  "feature": "CanCreateProjects",
  "message": "Projects are not available on your current plan.",
  "requiredPlan": "Starter"
}
```

### Phase 4 — Frontend error handling

Axios interceptor or per-call catch for HTTP 402:
- Show an upgrade prompt dialog: "This feature requires the **Starter** plan or higher."
- Link to pricing/upgrade page (future)

Create a composable `usePlanLimit()`:
```ts
const { handlePlanError } = usePlanLimit()
// in catch: if (isPlanLimitError(err)) handlePlanError(err)
```

### Phase 5 — UI hints (read-only gating)

Load current user plan on app init (store in `useAuthStore` or new `usePlanStore`).  
In sidebar/nav: show lock icons on gated features so users know before hitting the limit.

Example: If `canCreateOrganizations = false`, show Organizations nav item as disabled with a tooltip "Available on Pro plan".

---

## File Checklist (when implementing)

- [ ] `Services/Interfaces/ISubscriptionGatewayService.cs`
- [ ] `Services/SubscriptionGatewayService.cs`
- [ ] `Exceptions/PlanLimitException.cs`
- [ ] Middleware/global handler for `PlanLimitException` → 402
- [ ] Hook into: `ProjectsController`, `OrganizationsController`, `RunnersController`, `FlowsController`, `AppsController`, `StepsController`
- [ ] Frontend: `usePlanLimit()` composable
- [ ] Frontend: Axios 402 handler → upgrade dialog
- [ ] Frontend: `usePlanStore` — load current user plan on login
- [ ] Frontend: UI hints (lock icons, disabled nav items)

---

## Notes

- **No fake-free gating yet** — during development, assign yourself "Business" plan in admin UI and test freely.
- Gating should be **additive only** — never remove access from a user who already has resources. `MaxFlows = 5` means can't create new ones beyond 5, not that existing ones are deleted.
- Count queries for limits: use `_db.Flows.CountAsync(f => f.UserId == userId)` before insert.
