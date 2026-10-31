# Installed app store

Status: planned (31 October 2026).

## Decision

Every Automaton installation ships a public app store at `store.<domain>`. It is a separate frontend image released, installed and updated together with API, Worker and Studio. Install and update remain one operation each.

- The store renders the existing public marketplace API (`api/public/marketplace`). Listing stays opt-in per app (`CatalogVisibility == Listed`); launching still requires `RuntimeVisibility == Public`.
- Host: `store.<domain>`, not the bare `apps.<domain>`. `*.apps.<domain>` serves untrusted user apps, which can scope cookies to `apps.<domain>`; the bare `apps` host also already routes to the API. A sibling host keeps the store isolated and avoids path carving.
- automaton.monster keeps marketing and docs. Its `/apps*` routes redirect to `store.automaton.monster` (one store codebase). DNS record for `store.automaton.monster` exists (DNS only, same origin).
- Instance setting `Store: public | disabled`, default **public**. `members-only` is a follow-up (needs cross-origin authentication).

## Work

### 1. Store frontend — `src/automaton-store-vue`

- Move from `src/automaton-vue`: `AppsView`, `AppMarketplaceDetailView`, `CreatorStoreView`, `CreatorAppDetailView`, `MarketplaceAppRow`, `useMarketplaceApps`, marketplace types and asset URL helpers.
- Runtime configuration from `globalThis.__AUTOMATON_CONFIG__` (Studio pattern): `apiBaseUrl`, `publicAppsBaseUrl`, `studioUrl`, `storeTitle`, optional `logoUrl`. No automaton.monster defaults in production builds.
- Minimal layout: instance title/logo, search, categories, app and creator pages, "Powered by Automaton" footer. Disabled-store page when the API reports the store disabled.
- Tests for the runtime config loader and the disabled state.

### 2. Marketing site

- Replace `/apps`, `/apps/:appId`, `/creators/**` with redirects to `store.automaton.monster` (preserve path).
- Home-page `AppsDiscoveryStrip` keeps reading the API; its links point to the store.
- Deploy only after the store release is live on production.

### 3. Image and release — `.github/workflows/server-release.yml`

- `docker/Dockerfile.store` (node build → `nginx:1.27-alpine` with `docker/nginx/spa.conf`).
- Add `store` to the image matrix with an npm check step; write `STORE_IMAGE=` to `release.env`; include it in the anonymous pull verification.

### 4. Installation — `docker/install/compose.yaml`, `configure.py`, `install-automaton.sh`

- `store` service: `${STORE_IMAGE:?}`, mounts `store-runtime-config.js`, depends on healthy API.
- `configure.py`: write `store-runtime-config.js`; add `https://store.<domain>` to `Cors.AllowedOrigins`; Traefik service and `Host(\`store.<domain>\`)` router; the router obtains its certificate through the configured resolver in both TLS modes.
- Installer DNS message lists `store`; post-install check requests `https://store.<domain>/` (warning, not failure).

### 5. Update — `update-automaton.sh`

Implemented in #460: the updater compares the effective Compose configuration and allows added or changed application services while MySQL, RabbitMQ, Traefik, volumes, secrets and networks stay identical and no service is removed. It pulls and starts only release-owned application services (not override additions), runs `configure.py --ensure-store` after the verified backup, and only warns when the store does not answer. The manifest may gain `STORE_IMAGE`. Updaters installed by earlier releases still refuse the first store release, so that one update uses the downloaded `update-automaton.sh` (documented in the install guide). The store image's GHCR package starts private and must be made public before the first store release passes the anonymous pull check.

### 6. Store setting — API and Studio

Implemented: `Store.Settings` (`public` default, `disabled`), `GET/PUT api/admin/settings/store`, public `GET api/public/store/status`; marketplace and creator public endpoints return 404 when disabled; toggle on **Admin → Marketplace Apps**; the store shows a turned-off page. The marketing site redirects `/apps*` and `/creators/*` to the store (nginx 301 plus client-side), deployed manually after production runs the store release.

- Installation setting `StoreMode` (`Public` default, `Disabled`), persisted with existing installation settings.
- `PublicMarketplaceController` returns 404 for catalog endpoints when disabled; expose a small status endpoint the store can read.
- Toggle in Studio **Admin → Installation**.

### 7. Tests

- `scripts/tests/server-installer-config-test.py`: store config, route, CORS, idempotent migration of an existing configuration.
- `scripts/tests/server-updater-test.py`: upgrade from a release without `STORE_IMAGE`; missing store DNS produces a warning only.
- `scripts/tests/server-install-smoke.sh`: request the store.
- API tests for `StoreMode` enforcement.

### 8. Documentation

- Single source of truth in `docs/prod/install-automaton.md`; README and `src/automaton-vue/docs/install-server.md` summarize and link.
- DNS table: `api` (CLI, Runner broker, API), `studio` (administration), `store` (public app store), `apps`/`*.apps` (deployed apps). Port 5671 explained as the Runner broker everywhere.
- Mention the store in "What you get", the started-services lists and updater text; document listing in the store as separate from deploying (link `publish.md`).
- Short architecture overview near the top of the README: CLI → API/Studio → Runners → apps; store reads the API.

## Follow-ups

- `members-only` store mode with authentication through Studio/API.
- Cross-instance catalog feed or federation, only on demonstrated demand.
