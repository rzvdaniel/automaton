# Automaton.Studio – Low‑Risk Validation & Infrastructure Plan

## Goal
Validate Automaton.Studio as a real, useful platform **without upfront AI costs**, while keeping infrastructure simple, predictable, and scalable.

This plan is optimized for a **solo developer**.

---

## Core Strategy

### 1. Build in Public + Dogfood
- Use Automaton.Studio yourself to build real apps:
  - Poker planning app
  - Small tools
  - Internal utilities
- Publish them publicly via Automaton.
- Let real users interact with them.

This proves:
- App quality
- Hosting reliability
- End‑to‑end workflow

---

### 2. AI Usage Model: BYO API Key
- Users bring their own OpenAI (or compatible) API key
- You **do not pay for AI usage**
- No surprise bills
- High‑signal early users only

Optional later:
- Small demo quota funded by you
- Subscription with managed AI

---

## Runner Architecture (Key Decision)

### Existing Asset
You already have:
- A **published Docker container** for backend logic execution

This is perfect.

### Runner Model
- **One container instance per user/app**
- Deployed on a shared Hetzner server
- Isolated via Docker

Benefits:
- Strong isolation
- Easy lifecycle management
- Reuse existing image
- No redesign required

---

## Recommended Infrastructure (Phase 1)

### Hetzner Server
| Spec | Value |
|----|----|
| Plan | CPX21 |
| vCPU | 3 |
| RAM | 4 GB |
| Storage | NVMe |
| Cost | ~€6–7 / month |
| OS | Ubuntu 22.04 |

Enough for:
- Dozens of light runner containers
- Early public apps
- Your own usage

---

## Docker Runner Isolation Rules

Per container:
- CPU limit (e.g. 0.5–1 core)
- Memory limit (512 MB – 1 GB)
- Execution timeout
- No privileged mode
- Read‑only filesystem where possible

This prevents:
- Abuse
- Noisy neighbors
- Server crashes

---

## Execution Flow

1. User publishes app
2. Automaton API:
   - Spins up container from your image
   - Injects:
     - App config
     - User API key
     - Resource limits
3. App runs backend logic
4. Container can be:
   - Stopped on idle
   - Restarted on demand
   - Destroyed on deletion

---

## UX Positioning (Important)

**Not marketed as SaaS (yet)**

Position as:
> “A platform I use to instantly build and publish web apps.”

Users can:
- Try it
- Fork apps
- Bring their own AI key
- Publish for free

---

## Cost Reality (Phase 1)

### Fixed Costs
- Hetzner server: ~€7 / month

### Variable Costs
- AI: **€0**
- User pays their own OpenAI bill

This gives you:
- Predictable spend
- Zero scaling fear

---

## What You Validate

- Do users publish apps?
- Do they come back?
- Do they share apps?
- Do they complain about BYO API keys?
- Do they ask for managed AI?

These signals tell you **when to monetize**.

---

## Monetization Trigger Signals

Introduce subscriptions only when:
- Users publish real apps
- Apps get traffic
- Users hit OpenAI limits
- Users ask for simpler setup

---

## Future Phases (Optional)

### Phase 2 – Managed AI Credits
- Include small AI allowance
- Still allow BYO
- Learn cost curves

### Phase 3 – Subscription
- €25/month
- Managed AI
- One‑click experience
- Predictable margins

---

## Why This Plan Works

- Minimal upfront cost
- No AI risk
- Real validation
- Scales only when needed
- Uses assets you already have

---

## Summary

This approach lets Automaton.Studio grow **organically and safely**, driven by real usage, not assumptions.

You stay in control.
