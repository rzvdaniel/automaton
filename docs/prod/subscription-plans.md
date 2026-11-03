# Subscription Plans (instance owners)

Subscription plans let the owner of a self-hosted Automaton server set limits and capabilities for the people they open the server to: their team, their customers or their own users. If you run Automaton only for yourself, you do not need plans. Automaton does not ship preset plans or prices; you define your own.

Manage plans in Studio under **Admin → Subscription Plans** (administrators only; API: `api/admin/subscription-plans`).

## What a plan holds

| Field | Meaning |
|---|---|
| Name, Description | Shown in the plan list and in the assignment picker. |
| Subscription Price (USD/month) | `0` means free. A price above zero marks the plan as paid in the user-facing pricing page. |
| Stripe Product ID, Stripe Price ID | Optional. Only needed if you charge your users through your own Stripe account. |
| Default plan for new users | The plan assigned automatically to users created after you set it. Only one plan is the default, and only an active plan can be. |
| Active | Inactive plans stay in the list but are hidden from user-facing pricing and checkout and cannot be picked when assigning. |
| Sort Order | Order of plans. |
| Capabilities | The limits below. |

## Capabilities that are enforced today

Scheduled actions, per user:

| Setting | Default |
|---|---|
| Enabled | on |
| Maximum schedules | 10 |
| Minimum interval (seconds) | 300 |
| Maximum concurrent executions | 1 |
| Monthly execution limit | 5000 |

Values that are zero or missing are replaced by these defaults. Administrators are not limited by plans.

The capability settings are stored with the plan and are read by the API when a user creates or runs a schedule. The current Studio plan dialog does not show these fields; the API accepts them in the plan's `capabilities` object.

## Assigning plans

- The **User Plans** table on the same page lists users and their plan. **Assign** sets a user's plan, or clears it (None).
- A user with no plan gets the limits of the built-in free plan record if your database has one, otherwise the defaults above.
- Plan creation, changes and assignments are audit logged.

## Not available

Plans do not currently limit projects, organizations, AI spend, storage or Runners. Do not describe them to your users as if they do.
