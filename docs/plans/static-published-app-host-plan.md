# Static Published App Host Plan

## Goal

Serve public published Automaton apps as real top-level static websites on a dedicated app host:

```text
studio.automaton.monster  -> Vue Studio, editor, authenticated preview
api.automaton.monster     -> ASP.NET API control plane and runtime calls
apps.automaton.monster    -> public static published app files
```

This fixes PWA installation behavior because browser install metadata belongs to the published app document, not the Studio shell. It also moves normal visitor traffic away from ASP.NET controllers and into nginx/static serving, where Cloudflare can cache assets.

No backwards compatibility is required. This platform is still under development, so the implementation should replace the dynamic `/web/{slug}` serving model rather than preserve it indefinitely.

## Current Model

Automaton already has the correct workspace/runtime split:

```text
AppData/apps/{ownerId}/{appId}/
├─ source/
│  ├─ frontend/
│  └─ backend/
├─ live/
│  ├─ frontend/
│  └─ backend/
└─ versions/
```

- `source/` is editable workspace content for Studio, CLI, import/export, forks, and version snapshots.
- `live/frontend/` is deployed browser runtime output.
- `live/backend/` is runner-only backend runtime output and must never be publicly served.

The new app host should not introduce a parallel "drafts" model. It should add a public projection generated from `live/frontend`.

## Target Storage

Keep existing workspace storage:

```text
/app/AppData/apps/{ownerId}/{appId}/source/...
/app/AppData/apps/{ownerId}/{appId}/live/frontend/...
/app/AppData/apps/{ownerId}/{appId}/live/backend/...
```

Add a public published app projection:

```text
/app/AppData/published-apps/
├─ {slug}/
│  ├─ index.html
│  ├─ manifest.webmanifest
│  ├─ automaton-config.js
│  ├─ assets/
│  └─ icons/
└─ .staging/
```

In Docker production this must be a shared named volume:

- API mounts it read-write so publish/unpublish can create and remove app packages.
- nginx mounts it read-only so `apps.automaton.monster` can serve files directly.

Example:

```yaml
services:
  nginx:
    volumes:
      - prod-published-apps:/var/www/automaton-apps:ro

  api-blue:
    volumes:
      - prod-published-apps:/app/AppData/published-apps

  api-green:
    volumes:
      - prod-published-apps:/app/AppData/published-apps

volumes:
  prod-published-apps:
```

## Public URL Contract

Public app:

```text
https://apps.automaton.monster/{slug}/
https://apps.automaton.monster/{slug}/manifest.webmanifest
https://apps.automaton.monster/{slug}/automaton-config.js
https://apps.automaton.monster/{slug}/assets/...
```

Studio preview and private app access:

```text
https://studio.automaton.monster/apps/{appId}
```

The Studio `/web/{slug}` viewer should be removed or redirected once `apps.automaton.monster` is live. Public marketplace cards, dashboards, publish dialogs, and app settings should link to `apps.automaton.monster/{slug}/`.

## Publish Flow

Publishing is a file export operation owned by the ASP.NET API.

1. Authorize the user can update the app.
2. Validate the slug and uniqueness.
3. Resolve the app owner and verify `live/frontend/index.html` exists.
4. Create a staging directory:

```text
published-apps/.staging/{slug}-{appVersion}-{timestamp}/
```

5. Copy all allowed files from `live/frontend/` into staging.
6. Generate or update `automaton-config.js`:

```js
window.__AUTOMATON_APP__ = {
  slug: "iss-tracker-v2",
  appId: "00000000-0000-0000-0000-000000000000",
  apiBaseUrl: "https://api.automaton.monster"
};
```

7. Ensure `index.html` loads the runtime config and SDK.

Preferred publish-time injection:

```html
<script src="/runtime/automaton-api.js"></script>
<script src="./automaton-config.js"></script>
```

8. Ensure `manifest.webmanifest` exists. If the app did not provide one, generate a minimal manifest from app metadata.
9. Atomically replace `published-apps/{slug}` with the staging directory.
10. Update the database:

```text
IsPublished = true
PublicUrl = slug
PublishedAt = now
Version = Version + 1
```

11. Log publish activity.

Do not rewrite HTML on every request. Any required HTML/config patching happens at publish time.

## Unpublish Flow

1. Authorize the user can update the app.
2. Set `IsPublished = false`.
3. Remove public static files for the slug.

V1 can delete:

```text
published-apps/{slug}
```

Safer V1 can move the folder:

```text
published-apps/.disabled/{slug}-{timestamp}
```

Keep `PublicUrl` in the database so the same slug can be reused by the same app when republished.

## Static Host

Add an nginx vhost for `apps.automaton.monster`.

Required behavior:

- Serve files from `/var/www/automaton-apps`.
- Redirect `/{slug}` to `/{slug}/`.
- Serve `/{slug}/` as `/{slug}/index.html`.
- Serve `/{slug}/assets/*` directly.
- SPA fallback within a slug should return `/{slug}/index.html`.
- Unknown slug returns `404`.
- Never serve dot directories such as `.staging` or `.disabled`.

Caching:

```text
/{slug}/index.html                  Cache-Control: public, max-age=0, must-revalidate
/{slug}/manifest.webmanifest        Cache-Control: public, max-age=300
/{slug}/automaton-config.js         Cache-Control: public, max-age=0, must-revalidate
/{slug}/assets/*                    Cache-Control: public, max-age=31536000, immutable
/runtime/automaton-api.js           Cache-Control: public, max-age=300
```

Cloudflare can cache these responses because DNS is already proxied. R2 and Workers are not required for V1.

## Runtime API

Published apps should call:

```text
https://api.automaton.monster
```

The app SDK should be a static JavaScript file served from:

```text
https://apps.automaton.monster/runtime/automaton-api.js
```

The SDK reads:

```js
window.__AUTOMATON_APP__
```

Then calls API endpoints such as:

```text
POST https://api.automaton.monster/api/web-apps/{slug}/controllers/{controller}/actions/{action}/execute
POST https://api.automaton.monster/api/web-apps/{slug}/flows/{flow}/execute
```

## Private And Organization Apps

Do not publish private/org-only apps to `published-apps/` in V1.

V1 behavior:

- Unpublished workspace apps: Studio authenticated preview.
- Personal private apps: Studio authenticated preview.
- Organization internal apps: Studio authenticated route.
- Public published apps: `apps.automaton.monster/{slug}/`.

An authenticated private runtime on `apps.automaton.monster/o/{orgSlug}/{appSlug}/` is a future feature. It needs an auth gateway before file serving and should not be mixed into the public static host implementation.

## Backend Changes

Add a published app storage service, for example:

```text
PublishedAppStorageService
```

Responsibilities:

- `PublishAsync(ownerId, appId, slug, metadata)`
- `UnpublishAsync(slug)`
- copy `live/frontend` to staging
- validate public file paths and extensions
- generate `automaton-config.js`
- generate fallback manifest when missing
- atomically replace public slug folder

Update `AppsService.Publish`:

- Validate app and slug as today.
- Call `PublishedAppStorageService.PublishAsync`.
- Then update DB publish fields.

If DB update fails after file publish, clean up the public folder or leave a clear compensating log. Prefer staging first, DB update second, final folder swap last if practical.

Update `AppsService.Unpublish`:

- Update DB.
- Remove or disable the public folder.

## Frontend Changes

Replace public app URL construction from:

```text
/web/{slug}
```

to:

```text
{VITE_PUBLIC_APPS_BASE_URL}/{slug}/
```

Add frontend env var:

```text
VITE_PUBLIC_APPS_BASE_URL=https://apps.automaton.monster
```

Apply this to:

- app cards
- dashboard published apps
- publish dialog
- app settings public URL field
- marketplace app links
- preview overlay install CTA

Studio preview should keep showing that installation is available only from the published app URL.

## Remove Dynamic Serving

Once the static host works, remove or shrink the dynamic public serving paths:

- `PublicWebAppsController.ServePublishedAppDocument`
- `PublicWebAppsController.ServePublishedAppRuntimeFile`
- per-request HTML rewriting
- per-request generated `automaton-api.js`
- Vue `WebAppView` route if `/web/{slug}` is no longer used

Keep API endpoints that return metadata or execute app runtime actions.

## Implementation Order

1. Add config for `PublishedApps:RootPath`, `PublishedApps:PublicBaseUrl`, and `PublishedApps:ApiBaseUrl`.
2. Add Docker shared volume for published app files.
3. Add `apps.automaton.monster` nginx template and production render/deploy support.
4. Add static `/runtime/automaton-api.js` to the app host.
5. Implement `PublishedAppStorageService`.
6. Update publish/unpublish to create/remove static public packages.
7. Update frontend public app URL generation to use `VITE_PUBLIC_APPS_BASE_URL`.
8. Remove the dynamic `/web/{slug}` public serving path.
9. Validate PWA install on Android Chrome using `apps.automaton.monster/{slug}/`.
10. Document Cloudflare cache rules for `apps.automaton.monster`.

## Validation Checklist

- Unpublished app is not reachable on `apps.automaton.monster`.
- Published app loads from `apps.automaton.monster/{slug}/`.
- Deep links under a slug load the app through SPA fallback.
- `manifest.webmanifest` is served from the app host.
- icons referenced by the manifest load from the app host.
- `automaton-config.js` has the right slug, app id, and API base URL.
- app backend calls still go to `api.automaton.monster`.
- unpublish removes or disables public app files.
- private/org apps remain accessible only through Studio authenticated routes.
- Cloudflare caches hashed assets but revalidates `index.html` and config.
