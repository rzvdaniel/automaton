---
name: studio-design
description: Apply Automaton Studio’s established design patterns, theme tokens, and visual verification when changing user-facing Studio UI.
---

# Automaton Studio Design Skill

Use this skill for any user-facing Automaton Studio UI work.

This skill defines the visual/product design rules for Studio. It complements the Vue implementation skill.

The goal is consistency, not invention.

Automaton Studio should feel like one product across:

- Overview
- Deployments
- Runtime
- Logs
- Actions
- Source snapshots
- Publishing
- Settings
- Runners
- Projects
- Organizations
- API Keys
- other Studio management surfaces

When changing Studio UI, prefer existing Automaton patterns over framework defaults, generic design advice, or remembered PrimeVue conventions.

---

# 1. Core principle

Before creating a new visual treatment:

1. Find an existing Studio surface that expresses the same concept.
2. Inspect the actual component, CSS, and design tokens it uses.
3. Reuse that pattern where practical.
4. Introduce a new visual pattern only when no existing pattern fits.

Do not invent a new card, status, button, warning, empty-state, or page-layout language when Studio already has one.

Repository reality is authoritative.

---

# 2. Canonical visual references

Use these as primary references.

## Operational dashboard

Reference:

- App Overview

Use it for:

- operational status summaries
- primary deployment/runtime state
- compact secondary status cards
- operational diagnostics
- context-aware next actions

## Management cards

Reference:

- Publishing

Use it for:

- management panels
- compact cards
- access/state summaries
- primary management actions

## Forms and settings

Reference:

- App Settings

Use it for:

- settings pages
- forms
- section navigation
- labels/help text
- destructive-action separation

## Summary/stat cards

Reference:

- Runners

Use it for:

- counts
- status summaries
- dashboard cards
- operational collections

Do not merely approximate these pages from memory. Inspect their current implementation.

---

# 3. PrimeVue first

Prefer existing PrimeVue components over bespoke HTML/CSS where they express the intended interaction.

Prefer:

- `Card`
- `Button`
- `Tag`
- `Message`
- `Dialog`
- `Menu`
- `Popover`
- `InputText`
- `Textarea`
- `Select`
- other already-used PrimeVue controls

Avoid manually recreating:

- buttons
- tags
- cards
- alerts
- dialogs
- menus

unless the current Studio implementation genuinely requires custom behavior.

A raw custom control should be the exception, not the default.

---

# 4. Theme-token rules

Do not assume a PrimeVue CSS token exists because:

- its name sounds correct;
- it exists in framework documentation;
- it existed in another project;
- it is remembered from prior PrimeVue versions.

Before using a theme token:

1. verify it is defined in this Studio theme, or
2. verify it is already used successfully in current Studio code.

Prefer repository-verified tokens over framework assumptions.

## Current canonical panel/card background

Normal Studio cards/panels use the same background treatment as existing Overview/Publishing/Settings surfaces.

Current known canonical content/card background token:

```css
var(--p-content-background)
```

Do not substitute:

```css
var(--p-surface-card)
```

unless the active Studio theme actually defines it.

If a token is not defined, the resulting component may show the page background and appear visually incorrect.

## Rule

Repository-verified design tokens override PrimeVue documentation and model memory.

---

# 5. Cards and panels

Normal Studio cards should use the established neutral panel language.

Prefer:

- PrimeVue `Card`
- neutral content background
- subtle neutral border
- established Studio border radius
- established subtle shadow where used
- consistent padding
- consistent spacing between cards

Do not use status color as permanent structural decoration.

Avoid:

- green left borders for normal/success cards
- orange left borders for warning cards
- red structural borders for error cards
- tinted whole-card backgrounds unless an existing Studio pattern uses them intentionally

Status belongs in:

- `Tag`
- `Message`
- icon
- concise status text

not in the basic structural frame of the card.

---

# 6. Status presentation

Use status presentation consistently.

## Compact state

Use `Tag`.

Examples:

- Running
- Degraded
- Published
- Unpublished
- Offline
- Failed
- LIVE

## Warning/error callout

Use `Message` or an established Studio alert pattern.

Use it for:

- operational warnings
- degraded state explanation
- failed operations
- incomplete setup
- blocking configuration issues

Do not build a custom alert card if `Message` already fits.

## Avoid unsupported conclusions

Do not label a runtime:

```text
Healthy
```

unless the backend actually reports health.

Prefer factual state:

```text
Running
Runtime offline
Deployment failed
No runtime assigned
```

---

# 7. Buttons and actions

Prefer PrimeVue `Button`.

## Primary actions

Use a filled primary button for the dominant mutation/action on a page.

Examples:

- Add environment
- Publish Live
- Install Self-Hosted Runner

There should usually be one visually dominant primary action per page/section.

## Secondary navigation/actions

Prefer:

- text button
- outlined button
- subtle action link

Examples:

- View runtime
- View deployments
- Manage publishing
- Open preview
- View logs

Avoid bespoke raw `<button>` styles where a PrimeVue Button variant is sufficient.

---

# 8. Page structure

Studio pages should use a consistent hierarchy.

Typical structure:

```text
eyebrow / section context
H1
short page description

primary content

secondary sections
```

Maintain consistency in:

- content max width
- top padding
- section spacing
- card gaps
- heading scale
- body text width

Do not create unusually narrow/wide page layouts without a product reason.

---

# 9. Typography

Reuse existing Studio typography.

Prefer the same treatment already used on sibling pages for:

- eyebrow text
- H1
- H2/card headings
- muted help text
- status labels
- action text

Do not introduce new typography scales or custom letter spacing casually.

If using uppercase section labels, verify that pattern already exists.

---

# 10. Spacing

Use existing Studio spacing rhythm.

Avoid arbitrary local values if an existing nearby component already provides a good reference.

Before adding:

```css
margin: ...
padding: ...
gap: ...
```

inspect at least one canonical sibling page.

The goal is visual rhythm across Studio, not just internal consistency inside one component.

---

# 11. Operational-page rules

For app operational pages:

```text
Overview
Deployments
Runtime
Logs
Publishing
```

keep concepts distinct.

## Overview

Summarizes:

- current deployment
- Live URL
- runtime status
- publishing state
- immediate operational issue

Do not duplicate whole feature pages.

## Deployments

Owns:

- deployment targets/environments
- deployment state
- target-level actions
- CLI deploy guidance
- target configuration

Do not turn it into a second Overview.

## Runtime

Owns:

- runtime assignment
- runtime availability
- runtime-level operations

Do not expose infrastructure-admin internals to normal app owners.

## Logs

Owns:

- log context
- timestamps
- severity
- target/runtime context
- diagnostics

Do not redesign log transport from the UI layer.

## Publishing

Owns:

- published/unpublished state
- public/published URL
- access/visibility
- Publish/Unpublish

Publishing and Settings must not be duplicate destinations.

---

# 12. Source and CLI ownership

Studio is not the normal source-authoring environment.

Product boundary:

> Build locally with your AI. Deploy with Automaton CLI. Operate and publish in Automaton Studio.

Studio must not reintroduce:

- source editor
- browser file mutation
- browser AI coding
- runtime-contract editing as normal authoring
- source-deploy buttons

Where source changes are needed, guide users to the CLI.

Use verified commands only.

---

# 13. Local profile vs deployment target

Do not confuse:

```text
local project profile
```

with:

```text
remote deployment target/environment
```

Local profiles belong to CLI/project configuration.

Studio generally talks about:

- Live environment
- additional environment
- deployment target

Do not leak local CLI profile terminology into Studio unless directly relevant.

---

# 14. Reuse before abstraction

Do not automatically create a shared component just because two pages look similar.

Preferred order:

1. reuse existing PrimeVue primitive;
2. reuse existing Studio component;
3. copy a small established styling pattern if appropriate;
4. extract a shared component only when repetition is real and stable.

Avoid premature generic components such as:

```text
UniversalCard
GenericDashboardSection
AllPurposeStatusPanel
```

A small, stable primitive is acceptable if repeated enough.

---

# 15. New visual patterns require justification

If you introduce a new visual treatment, explain in the PR:

- why existing Studio patterns were insufficient;
- what new pattern is being introduced;
- where else it should be reused;
- whether it should become a shared primitive.

If no justification exists, reuse an existing pattern instead.

---

# 16. Repository inspection requirement

For every Studio UI task involving a new or modified page/card layout:

Inspect at least two relevant sibling surfaces before implementation.

Examples:

```text
Overview + Publishing
Publishing + Settings
Overview + Runners
Deployments + Runtime
```

Do not style from memory.

---

# 17. Light and dark mode

When modifying visible Studio surfaces:

Check both:

- light mode
- dark mode

Do not assume a token that looks correct in light mode will work in dark mode.

Avoid literal color values unless an established Studio pattern already uses them.

Prefer theme tokens.

After checking dark mode, restore the user's original theme during local verification.

---

# 18. Responsive behavior

At minimum verify the page at:

- normal desktop width
- narrower desktop/tablet-style width when practical

Cards should:

- wrap or stack naturally;
- avoid horizontal overflow;
- retain readable hierarchy;
- keep actions accessible.

Do not optimize for one screenshot width only.

---

# 19. Empty, loading, error, and degraded states

Every operational surface should consider:

- loading
- empty
- normal
- warning/degraded
- failure

Use existing Studio primitives.

Do not leave large blank regions or generic placeholder text.

Empty states should explain:

```text
what is missing
what the user can do next
```

without excessive copy.

---

# 20. Visual verification checklist

Before considering a Studio UI change complete:

- [ ] Compared against at least two relevant Studio pages
- [ ] Reused existing PrimeVue/Studio primitives
- [ ] Verified all new theme tokens exist in the current theme/repository
- [ ] No new card visual language introduced unnecessarily
- [ ] No bespoke button styling where PrimeVue would work
- [ ] Status uses Tag/Message/icon/text rather than structural card color
- [ ] Light mode checked
- [ ] Dark mode checked
- [ ] Desktop layout checked
- [ ] Narrower layout checked if the change affects responsive composition
- [ ] Loading/empty/error/degraded states considered
- [ ] No source-authoring behavior accidentally reintroduced

---

# 21. Manual comparison requirement

For meaningful UI changes, use the local Studio browser if available.

Compare the changed page against relevant canonical pages at the same viewport.

Do not only inspect the changed page in isolation.

When reporting completion, state which pages were compared.

Example:

```text
Manual visual comparison:
- Deployments
- Overview
- Publishing
- Runners
```

---

# 22. Tests

Use functional/component tests to protect meaningful UI structure and behavior.

Good tests:

- correct navigation destination
- correct PrimeVue component usage where structurally meaningful
- warning rendered for degraded state
- action shown/hidden under correct conditions
- publishing/settings remain distinct
- no browser authoring controls

Avoid brittle tests for:

- exact pixel spacing
- raw class counts
- implementation-only CSS details

Visual consistency should primarily come from reuse + browser verification + canonical primitives.

---

# 23. PR reporting

For Studio UI PRs, include:

```text
Visual references used
PrimeVue/Studio primitives reused
Any new visual pattern introduced
Theme tokens added/used
Light-mode verification
Dark-mode verification
Pages compared manually
```

If a new token or visual primitive is introduced, call it out explicitly.

---

# 24. Hard constraints

Do not:

- invent theme tokens;
- assume framework tokens exist;
- create custom card systems when PrimeVue Card fits;
- use colored card borders as routine status presentation;
- duplicate Publishing and Settings behavior;
- reintroduce Studio source authoring;
- mix local CLI profiles with remote deployment environments;
- expose infrastructure internals on normal app-owner pages;
- introduce a new design language inside one feature area.

When uncertain:

> Find an existing Automaton Studio surface that already solves the same visual problem and follow it.
