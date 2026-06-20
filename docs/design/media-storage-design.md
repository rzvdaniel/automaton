# Automaton Media Storage Design

## Purpose

Automaton apps can already contain static frontend assets, such as images bundled with the app UI. However, some apps also need user-generated files created at runtime.

Examples:

- Blog post images
- Uploaded documents
- Profile avatars
- Generated files
- Attachments

These files should not be treated as normal frontend static assets. They are runtime app data and should stay under the control of the app owner.

## Core Principle

Automaton media storage should be runner-owned, app-scoped, and portable.

In practice this means:

- For self-hosted runners, uploaded files live on the user's machine or infrastructure.
- For managed runners, uploaded files live in that user's isolated managed runner volume.
- Automaton provides safe routing, upload/download APIs, and export/import support.
- Automaton should not force users to use external image hosting or platform-owned central storage.

The product promise is:

```text
Your app.
Your backend.
Your files.
Portable by design.
```

## Static Assets vs Runtime Media

Automaton should clearly separate frontend static assets from runtime media.

### Frontend Static Assets

Examples:

```text
/dist/assets/logo.png
/dist/assets/app.css
/dist/assets/app.js
```

Characteristics:

- Created at build/deploy time.
- Usually immutable until the next deploy.
- Served by Automaton frontend hosting.
- Safe to cache aggressively.
- Part of the app frontend bundle.

### Runtime Media

Examples:

```text
/data/media/uploads/post-cover.jpg
/data/media/avatars/user-123.png
/data/media/documents/file.pdf
```

Characteristics:

- Created or modified while the app is running.
- Belongs to the app's runtime data.
- Stored on the runner, not in the frontend bundle.
- Must be included in app export/import.
- Should be protected by app-scoped access rules.

## Recommended Storage Layout

Each app should have a dedicated data directory on its runner.

Example layout:

```text
runner-data/
  apps/
    {appId}/
      source/
      runtime/
      data/
        media/
          uploads/
          images/
          documents/
```

For a Blog app, uploaded images could live under:

```text
runner-data/apps/{appId}/data/media/uploads/{fileName}
```

The public URL should not expose the physical storage layout.

Recommended public URL:

```text
/apps/{slug}/media/uploads/{fileName}
```

The Blog content should store relative media URLs such as:

```json
{
  "title": "My first post",
  "coverImage": "/media/uploads/cover.jpg"
}
```

This keeps the app portable and avoids hard-coding external storage providers.

## Initial Architecture

The first implementation should use an API proxy to the runner.

```text
Browser
  ↓
GET /apps/{slug}/media/uploads/image.jpg
  ↓
Automaton API
  ↓
Runner communication channel
  ↓
Runner reads app-scoped file
  ↓
API streams file back
  ↓
Browser
```

This is the simplest option because:

- It works with remote self-hosted runners.
- It works with managed runners.
- It does not require nginx or Traefik to access runner disks.
- It avoids dynamic runner routing infrastructure for now.
- It reuses the existing API-to-runner communication model.

The API should act as a gateway, not as the owner of the files.

## Future Architecture

Later, Automaton can optimize media serving without changing the app contract.

Possible future implementations:

1. Direct runner routing through a tunnel.
2. Managed runner local volume served by internal reverse proxy.
3. Optional object storage backend for managed runners.
4. CDN caching in front of public media URLs.

The public app contract should remain stable:

```text
/media/{path}
```

or, when accessed through a published app:

```text
/apps/{slug}/media/{path}
```

The underlying storage can evolve from runner disk to direct runner routing or object storage later.

## Upload Flow

A typical upload flow for the Blog app:

```text
Admin selects image
  ↓
Frontend sends file to backend/API
  ↓
Backend validates file
  ↓
Runner stores file under app data/media
  ↓
Backend returns a public media URL
  ↓
Blog post stores the relative media URL
```

Example response:

```json
{
  "path": "/media/uploads/2026/06/post-cover-abc123.jpg"
}
```

The app should store the returned path, not the physical file path.

## Download and Serving Flow

When a browser requests media:

```text
GET /apps/my-blog/media/uploads/cover.jpg
```

Automaton should:

1. Resolve the app slug to an app id.
2. Resolve the app id to a runner.
3. Validate that the requested media path is safe.
4. Ask the runner to stream the file.
5. Return the correct status code, content type, content length if known, and cache headers.

The runner should never allow access outside the app media directory.

## Security Rules

Media storage must be safe by default.

Minimum rules:

- Prevent path traversal, for example `../`.
- Normalize and validate paths before accessing disk.
- Restrict reads and writes to the app's media directory.
- Enforce maximum upload size.
- Enforce per-app storage quota.
- Validate MIME type and file extension.
- Prefer generated file names over user-provided names.
- Store original file names only as metadata when needed.
- Avoid serving executable files as executable content.
- Return safe content headers.
- Support private media later.

Recommended generated file pattern:

```text
/uploads/{yyyy}/{MM}/{safe-slug}-{randomId}.{extension}
```

Example:

```text
/uploads/2026/06/my-first-post-b8f41c.jpg
```

## Public vs Private Media

The first version can support public media only.

For example:

```text
/media/uploads/post-cover.jpg
```

Later, Automaton can add private media support.

Possible private media URL:

```text
/private-media/{path}
```

Private media would require authentication and app-level authorization before streaming the file.

For the Blog app, public images are enough for the first version.

## Export and Import

Media must be part of app portability.

An app export should include:

```text
frontend source/build
backend source
app configuration
runtime database/content if applicable
data/media
```

Example export archive:

```text
my-blog-export.zip
  app.json
  frontend/
  backend/
  data/
    media/
      uploads/
        cover.jpg
```

This allows users to relocate apps between:

- Self-hosted runner to managed runner
- Managed runner to self-hosted runner
- One managed runner to another managed runner
- Automaton to external hosting in the future

## Managed Runner Behavior

For managed runners, each user should have isolated storage.

Recommended model:

```text
managed-runner-{userId}
  volume:
    apps/{appId}/data/media
```

This keeps media ownership aligned with the user's runner.

The platform can back up the managed runner volume, but the files should still be considered app/user data, not platform-owned assets.

## Self-Hosted Runner Behavior

For self-hosted runners, files live wherever the user configured runner data storage.

Example:

```text
~/.automaton/apps/{appId}/data/media
```

or:

```text
/var/lib/automaton/apps/{appId}/data/media
```

Automaton should document this clearly so users understand where their uploaded files are stored and how to back them up.

## Blog App Usage

The Blog app should use the platform media contract instead of external image hosting.

Example post:

```json
{
  "id": "post-1",
  "title": "Hello Automaton",
  "slug": "hello-automaton",
  "coverImage": "/media/uploads/2026/06/hello-automaton-b8f41c.jpg",
  "content": "..."
}
```

In the frontend:

```html
<img src="/media/uploads/2026/06/hello-automaton-b8f41c.jpg" alt="Hello Automaton" />
```

When the app is published under:

```text
https://apps.automaton.monster/my-blog
```

The effective media URL becomes:

```text
https://apps.automaton.monster/my-blog/media/uploads/2026/06/hello-automaton-b8f41c.jpg
```

## Recommended First Version

The first version should implement:

- App-scoped media directory on the runner.
- Public media upload endpoint.
- Public media streaming endpoint.
- API proxy to runner for media reads/writes.
- Path safety checks.
- File size limit.
- Basic MIME type allowlist.
- Per-app quota.
- Export/import includes `data/media`.

Do not implement initially:

- CDN integration.
- Direct runner routing.
- Object storage abstraction.
- Private media.
- Image transformations.
- Virus scanning.

These can be added later.

## Open Questions

- Should media uploads go through the app backend, Automaton API, or both?
- Should Automaton provide a generic media API available to all apps?
- Should apps be able to define their own allowed MIME types?
- What should the default per-app media quota be for free and managed runners?
- Should media paths be app-relative only, or should Automaton expose absolute URLs in API responses?
- Should deleting an app delete media immediately, soft-delete it, or keep it for a retention period?

## Decision

Automaton should treat uploaded files as runner-owned app data.

The initial implementation should use API-proxied runner file streaming, while preserving a stable public media URL contract that can later be backed by direct runner routing, managed runner volumes, object storage, or CDN caching.

This gives Automaton a simple first implementation while preserving the long-term product promise of user-controlled, portable applications.
