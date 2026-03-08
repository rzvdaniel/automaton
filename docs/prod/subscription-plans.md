# Subscription Plans — Production Setup

Create these plans via **Admin → Subscription Plans** after deploying to production.

## Plans

| Field | Free | Starter | Pro | Business |
|---|---|---|---|---|
| **Name** | Free | Starter | Pro | Business |
| **Description** | Get started for free | For indie developers | For power users & small teams | For companies |
| **AI Budget (USD/month)** | 2.00 | 10.00 | 30.00 | 100.00 |
| **Can Create Projects** | ❌ | ✅ | ✅ | ✅ |
| **Can Create Organizations** | ❌ | ❌ | ✅ | ✅ |
| **Is Default** | ✅ | ❌ | ❌ | ❌ |
| **Sort Order** | 0 | 1 | 2 | 3 |

## Notes

- **Free is the default plan** — automatically assigned to new users on registration (once user plan auto-assignment is wired up).
- **AI budget** is enforced today: when a user exceeds their monthly limit, AI calls are downgraded to the cheapest available model.
- **Projects / Organizations** gating is planned but not yet enforced — see `docs/plans/feature-gating.md`.
- All plans allow unlimited self-hosted runners (no restriction).

## Rationale

- **$2 Free**: ~500–800 AI interactions/month with Gemini Flash Lite. Enough to evaluate and build something real, not enough to abuse.
- **$10 Starter**: Daily use for a solo developer. Adds Projects for structured work.
- **$30 Pro**: Heavy usage + team collaboration via Organizations.
- **$100 Business**: High-volume teams and companies.

## Future dimensions (not implemented yet)

| Dimension | Notes |
|---|---|
| Managed runner (Hetzner) | One provisioned runner per paying user — future add-on or plan perk |
| Storage limit | Runner container data, files, databases — add `StorageLimitGb` to plan when needed |
| Bandwidth | Not a concern — Cloudflare caches published app frontends |
