# Traefik Edge Consolidation Audit

Date: 2026-07-11

## Current production ownership

The public production ports are currently owned by `automaton-nginx`:

- `80:80` and `443:443`
- TLS certificates from `.prod-runtime/letsencrypt`
- ACME HTTP-01 files from `.prod-runtime/certbot-www`
- blue/green upstream selection for API, Studio, and Website
- published-app static files and private runtime static files
- app API forwarding to Traefik

Traefik is currently an internal service on the `automaton-runtime` network:

- entrypoint `web` on `:80`
- ping entrypoint on `:8082`
- file provider for generated managed-runner routes
- no TLS entrypoint
- no ACME resolver

## Route inventory

| Current nginx responsibility | Current target | Required Traefik target |
| --- | --- | --- |
| `api.automaton.monster` | selected `api-blue`/`api-green` upstream | API service with health and WebSocket support |
| `studio.automaton.monster` | selected Studio container | Studio service |
| `automaton.monster`, `www` | selected Website container | Website service |
| `apps.automaton.monster/{slug}` | `prod-published-apps` static volume | Published-app runtime route; ownership still needs explicit design |
| `/a/{appId}/...` | nginx static/private runtime plus `/api` to Traefik | managed runner `/apps/{appId}/...` |
| `/files`, `/web`, upload/download paths | API or app-specific nginx proxy rules | API or destination service; verify individually |
| `/.well-known/acme-challenge` | Certbot webroot | Traefik ACME HTTP-01 or retained external certificate automation |
| security and forwarded headers | nginx | Traefik middleware plus destination validation |

## Required migration order

1. Add Traefik `websecure` on `:443` and choose one certificate owner.
2. Add an API router and service, including WebSocket forwarding and request limits.
3. Validate API health, uploads, cookies, CORS, and SignalR.
4. Add Studio routing.
5. Add Website routing and SPA behavior.
6. Validate managed-runner frontend and backend routes.
7. Migrate published-app slug routing and decide whether published artifacts move to runners.
8. Move HTTP-01/certificate renewal ownership.
9. Switch host ports `80/443` to Traefik.
10. Remove nginx app volumes, templates, services, and reload scripts.

## Known blockers before port cutover

- Traefik has no TLS entrypoint or certificate resolver.
- Certbot and nginx currently own certificate files and HTTP-01 handling.
- Production Traefik cannot currently route to blue/green API, Studio, and Website services by Docker service discovery because those services are selected through nginx-generated upstream files.
- Published slug apps are still backed by the API-owned `prod-published-apps` volume.
- The local runtime compose stack still uses nginx as its TLS edge.

No Cloudflare Tunnel is required by this migration.
