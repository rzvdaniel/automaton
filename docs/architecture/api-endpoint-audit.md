# API endpoint audit after the Studio redesign (#843)

Checked on 2026-11-08 against Studio, the app store UI, the CLI, the Runner, the MCP client, catalog and system apps,
the install scripts and the docs. Studio pages were removed in #831; this lists what happened to their endpoints.

## Removed (no caller in Studio, store, CLI, Runner, MCP, apps, scripts or docs)

| Controller | Endpoints |
|---|---|
| `AppsStepsController` | `api/apps/{id}/steps` (link, create, list) |
| `AiStepConversationController`, `AiUtilityController`, `AiConversationController` (empty file) | `api/ai/step/conversation`, name generation |
| `AiApiKeysController` | `api/AiApiKeys` |
| `AdminAiProvidersController`, `AdminAiModelsController`, `AdminAiProviderKeysController`, `AdminAiSettingsController`, `AdminAiGuidanceController` | `api/admin/ai-providers`, `api/admin/ai-guidance` |
| `BillingController`, `AccountSubscriptionController`, `AdminSubscriptionPlansController` | `api/billing`, `api/Account` subscription, `api/admin/subscription-plans` |
| `DashboardController`, `AdminDashboardController`, `RecentActivitiesController` | dashboard stats, recent activity |
| `AdminMarketplaceAppsController` | `api/admin/marketplace-apps` |

Also removed: the Studio client methods for those calls (`apps.service.ts`) and the tests that only covered the removed controllers.

## Kept on purpose

| Endpoint | Client that needs it |
|---|---|
| `api/projects`, `api/projects/{id}/members`, `apps`, `runners` | CLI (`project`, `runners install`, `share`) and the Studio share dialog (`listMyTeams`) |
| `api/ai-guidance` (`AiGuidanceController`) | CLI (`AiGuidancePackService`) |
| `api/webhooks/stripe` (`StripeWebhookController`) | Stripe calls it; not a Studio caller |
| `PUT /api/admin/settings/store` | API-only app store on/off switch, documented in the Admin docs; no Studio page |
| `organizations/apps/assigned`, `organizations/{id}/apps/assigned` | Studio app list and organization dashboard |
| `user-settings` | Studio (`useUserSettings`) |
| `AiChatHistoryController` | AI agents / MCP chat history |

## Not removed yet (needs the owner's decision)

- `organizations/{id}/projects` endpoints and the AI model fields of `user-settings`: no caller, but they belong to the
  Project data model.
- Services, entities and tables behind the removed controllers (flows, steps, projects, AI keys and providers, plans).
  Dropping them needs a migration that deletes data, so it is a separate change that the owner approves first.
