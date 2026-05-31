# App Moderation

This document explains how app moderation currently works in the Automaton admin surface.

## Purpose

Admin moderation is intentionally separate from normal app ownership workflows.

Owners can build, edit, deploy, and publish their own apps.
Admins can inspect, moderate, and restrict apps without entering the user editor or acting as the owner.

Current admin surfaces:

- `/admin/users/{userId}/apps`
- `/admin/apps/{appId}`
- `/admin/marketplace-apps`

## Model

App moderation uses two fields on `WebApp`:

- `ReviewStatus`
- `AccessStatus`

### ReviewStatus

Allowed values:

- `approved`
- `flagged`

Meaning:

- `approved`: normal reviewed state
- `flagged`: needs admin attention or review

`ReviewStatus` primarily affects marketplace/public discovery.

### AccessStatus

Allowed values:

- `active`
- `hidden`
- `disabled`

Meaning:

- `active`: app is allowed to be publicly reachable
- `hidden`: removed from marketplace and blocked from public runtime access
- `disabled`: blocked due to enforcement, abuse, safety, or platform action

Important: `hidden` is currently a strict state, not "unlisted but reachable by link".

If we ever need "shareable by direct URL but not discoverable", introduce a separate value such as `unlisted` instead of weakening `hidden`.

## Effective Behavior

### Marketplace visibility

For an app to appear in public marketplace/discovery surfaces, all of these must be true:

- `IsPublished = true`
- `ReviewStatus = approved`
- `AccessStatus = active`

This is the public storefront rule.

### Direct public app access

For an app to be reachable on its public slug/runtime path, all of these must be true:

- `IsPublished = true`
- `AccessStatus = active`

This means:

- `hidden` blocks direct public serving
- `disabled` blocks direct public serving

### Forking and public-source-derived flows

Published app flows that depend on public visibility follow the same moderation intent:

- only publicly acceptable apps should be usable as public fork sources
- disabled apps should not remain runnable through older published paths

## Why Two Fields

One field was too ambiguous.

Examples:

- `flagged` is a review state, not necessarily an access block
- `hidden` is a visibility/access state
- `disabled` is an enforcement/access state

Separating review from access makes intent clearer:

- review answers: "does this app need moderation attention?"
- access answers: "is this app allowed to be public and reachable?"

## Legacy Compatibility

Older code used `MarketplaceStatus`.

That field still exists temporarily for compatibility, but admin behavior should treat these as the canonical fields:

- `ReviewStatus`
- `AccessStatus`

Legacy mapping currently works like this:

- `approved` -> `ReviewStatus=approved`, `AccessStatus=active`
- `hidden` -> `ReviewStatus=approved`, `AccessStatus=hidden`
- `banned` -> `ReviewStatus=flagged`, `AccessStatus=disabled`

Do not build new moderation logic on top of `MarketplaceStatus`.

## Admin UI Guidance

When describing the statuses in admin UI, keep the wording explicit:

- `Hidden`: removed from marketplace and public runtime access
- `Disabled`: blocked due to enforcement or safety

Avoid wording that implies `hidden` is still reachable by direct link, because that is not true in the current implementation.

## Audit Trail

Moderation changes are logged through `RecentActivityService`.

Expected audit descriptions include changes such as:

- `ReviewStatus approved -> flagged`
- `AccessStatus active -> disabled`
- `Note: ...`

This gives us a usable first audit trail without introducing a dedicated moderation audit table yet.

## Files

Backend:

- `src/Automaton.Studio.Server/Entities/WebApp.cs`
- `src/Automaton.Studio.Server/Services/AdminAppsService.cs`
- `src/Automaton.Studio.Server/Controllers/Admin/AdminMarketplaceAppsController.cs`
- `src/Automaton.Studio.Server/Controllers/PublicMarketplaceController.cs`
- `src/Automaton.Studio.Server/Controllers/UserAppsController.cs`
- `src/Automaton.Studio.Server/Controllers/AppFilesController.cs`
- `src/Automaton.Studio.Server/Services/PublicAppsService.cs`
- `src/Automaton.Studio.Server/Services/AppsService.cs`
- `src/Automaton.Studio.Server/Services/WebAppStarService.cs`

Frontend:

- `src/automaton-studio-vue/src/features/admin/views/AdminAppDetailView.vue`
- `src/automaton-studio-vue/src/features/admin/views/AdminMarketplaceAppsView.vue`

Migration:

- `src/Automaton.Studio.Server/Migrations/20260531125500_AddWebAppReviewAndAccessStatuses.cs`

## Future Options

Possible future improvements:

- add `unlisted` if we want direct-link access without marketplace discovery
- add a dedicated moderation audit table instead of relying on recent activity entries
- expose moderation history directly in `/admin/apps/{appId}`
- distinguish marketplace visibility from runtime availability even more explicitly if product needs diverge
