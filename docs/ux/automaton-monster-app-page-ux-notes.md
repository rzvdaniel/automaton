# Automaton Monster — Apps & App Details UX Notes

## Core Direction

Automaton Monster should feel like:

- a living app ecosystem
- a remixable deployment platform
- an AI-native app marketplace
- not a template marketplace
- not a generic no-code builder

The ecosystem loop is:

```text
AI → build → deploy → publish → fork → remix
```

---

# Global Navigation

Recommended permanent top navigation:

```text
Apps
Docs
Pricing

[Build an app]
```

## Build an app

The main CTA should redirect to:

```text
https://studio.automaton.monster
```

The wording should remain outcome-oriented:

GOOD:
- Build an app

AVOID:
- Open Studio
- Dashboard
- Launch Studio

---

# Docs

Docs should be publicly accessible.

Recommended:
- docs.automaton.monster
OR
- automaton.monster/docs

Avoid requiring authentication for documentation.

Docs are:
- onboarding
- SEO
- developer trust
- architecture explanation
- marketing

---

# Pricing

Current pricing model:

## Free
- static frontend apps
- public apps
- BYOK AI
- self-hosted runners
- CLI + Studio access
- public hosting
- fork/remix ecosystem

## Managed runners
Coming later.

Pricing page should be transparent and developer-oriented.

Avoid fake SaaS tiers until subscriptions are real.

---

# Apps Page Architecture

## Two-layer navigation

### Global navigation
Visible on all pages:

```text
Apps
Docs
Pricing
[Build an app]
```

### Apps discovery navigation
Visible ONLY on Apps/discovery pages:

```text
Featured | Games | AI Tools | Backend Apps | Most Forked

[ Search apps... ]
```

This discovery bar should NOT appear:
- on homepage
- on docs
- on pricing

---

# Homepage Direction

Homepage should focus on:

1. What Automaton is
2. How it works
3. Proof/examples
4. Ecosystem activity
5. CTA

Remove:
- large template sections
- secondary app store sections
- duplicated discovery experiences

The dedicated Apps page owns discovery.

---

# Ecosystem / Activity Section

Keep:
- ecosystem activity feed
- remix activity
- publishing updates
- creator activity

But avoid over-framing it around "Creators".

Better framing:
- Living ecosystem
- App activity
- Remix ecosystem
- Community activity

---

# Final Homepage CTA

Recommended direction:

```text
Build and publish your first app in minutes.

Install the setup flow, tell your AI what to build, then deploy directly to Automaton Monster.

[Copy setup flow]
```

The button should copy:
- setup command
- onboarding bootstrap
- CLI install flow

Example:

```bash
npx automaton setup
```

---

# Forking Model

Publishing and forking are separate concepts.

## Public app
Users can:
- play/use app
- open app page

## Forkable app
Users can additionally:
- fork source/project
- remix
- publish derivatives

Recommended publish settings:

```text
Visibility
(o) Public
( ) Private

Forking
[x] Allow others to fork this app
[ ] Keep source private
```

---

# Fork Button Rules

## If app is forkable
Show:
- Fork button
- Forkable badge
- fork count

## If app is NOT forkable
Do NOT show:
- disabled Fork button
- "Private source"
- locked states

Simply hide the Fork action entirely.

---

# Fork Flow

Clicking Fork should NOT silently clone the app.

Recommended flow:

```text
Apps page
→ click Fork
→ auth if needed
→ Fork App modal
→ create private editable copy
→ open Studio editor
```

## Fork modal example

```text
Fork “Pac-Man Editor and Game”

Create your own editable copy of this app.

App name:
[ Pac-Man Neon Remix ]

Visibility:
[x] Private draft

[ Fork app ]
```

---

# Fork Lineage

Forks should preserve attribution.

Example:

```text
Forked from:
Pac-Man Editor and Game by Razvan Predescu
```

Fork lineage is a core ecosystem feature.

---

# App Card UX

## Card click
Opens app detail page.

## Play
Launches app directly.

## Fork
Starts remix flow.

## Creator
Opens creator-related area/profile later.

---

# App Card Interaction Rules

All clickable elements should:
- use pointer cursor
- have hover states
- visually commit to interactivity

Avoid:
- hover borders without pointer cursor
- ambiguous clickability

---

# Recommended App Card Layout

```text
IMAGE

LIVE APP · FEATURED · FORKABLE

Pac-Man Editor and Game

Playable arcade remake with editor-style customization and mobile-friendly controls.

by Razvan Predescu

▶ 57 plays · 🍴 1 fork · updated today

[ Play ]    [ Fork app ]
```

---

# App Card Improvements

## Replace:
```text
57 views 1 forks 0 stars
```

With:
```text
▶ 57 plays · 🍴 1 fork · updated today
```

Forks matter more than stars in Automaton.

---

# Buttons

Play should NOT dominate visually.

Recommended:
- dark premium buttons
- subtle green accents
- green on hover
- avoid giant neon green bars

---

# App Detail Page Direction

The page should optimize for:

1. Try the app
2. Understand the app
3. Remix the app

---

# App Detail Page Hierarchy

## Top section
- badges
- title
- creator row
- short summary
- inline stats
- actions
- compact info pills

## Gallery
Keep prominent, but not dominant.

## Overview section
Contains:
- About this app
- Runtime
- Forks & Remixes
- More from creator

---

# Summary vs Description

## Short summary
Displayed near title/creator.

## Full description
Displayed in:
- About this app

---

# Recommended Badges

Examples:
- Live app
- Featured
- Forkable
- Static
- Backend app

Avoid too many badges.

---

# Metadata

Recommended inline metadata:

```text
▶ 57 plays · 🍴 1 fork · ★ 0 saves · updated today
```

Avoid:
- screenshot count
- large metadata cards

---

# Runtime Information

Compact runtime panel:

```text
Frontend: Static hosting
Backend: Not required
Fork & publish: Free
```

---

# Forks & Remixes

Important future ecosystem feature.

Eventually contains:
- popular remixes
- recent remixes
- fork lineage
- app evolution

Example:
- AI Ghosts Edition
- Neon Maze Remix
- Mobile Arcade Remix

---

# Tabs

Recommended NOW:
- keep mostly Overview-focused
- avoid empty/fake complexity

Current recommendation:
- Overview only
OR
- visual tabs with Overview as primary

Avoid fully separate sparse pages until ecosystem grows.

---

# Future Tabs

## Overview
Main landing section.

## Forks & Remixes
Dedicated remix ecosystem.

## Changelog
App updates/history.

## Creator
Eventually creator profiles/stats.

Only expand these once ecosystem density exists.

---

# Creator Section

"More from this creator" is good.

Keep:
- creator apps
- related projects
- ecosystem continuity

But avoid turning creators into a major product pillar too early.

---

# Long-Term Ecosystem Vision

Automaton should eventually feel like:

- GitHub for runnable apps
- Steam for AI-built web apps
- remixable deployment ecosystem
- AI-native software publishing platform

Core differentiation:
- runnable apps
- deployable apps
- remixable apps
- optionally closed-source apps


---

# Runtime vs Requirements

The term:

```text
Runtime
```

is technically correct, but may feel too developer-oriented for public-facing UI.

Users usually care more about:

```text
What do I need to run this app?
```

than:

```text
What runtime abstraction powers this app?
```

## Recommended public terminology

Prefer:

```text
Requirements
```

OR:

```text
App setup
```

instead of:

```text
Runtime
```

Especially on public app detail pages.

---

# Recommended Requirements Panel

Instead of:

```text
Runtime

Frontend: Static hosting
Backend: Not required
Fork & publish: Free
```

Prefer more user-friendly wording:

```text
Requirements

Runs entirely in the browser
No backend required
Free to fork and publish
```

For backend apps:

```text
Requirements

Requires backend execution
Needs a Runner container
Can run on your own infrastructure
Managed runners coming later
```

---

# Internal vs Public Terminology

## Internal/dev terminology
Using:

```text
Runtime
```

is fine internally and in technical docs.

## Public-facing UI
Prefer:

```text
Requirements
```

because it is:
- clearer
- less technical
- more approachable
- more user-focused
