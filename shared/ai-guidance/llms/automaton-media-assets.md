---
title: "Automaton Media and Static Asset Rules"
description: "Bundled assets versus runtime media, uploads, and public/static asset handling."
---

# Automaton Media and Static Asset Rules

Use this file when building or modifying an Automaton app that has images, documents, uploads, generated files, blog media, avatars, attachments, or other app-owned resources.

## Core distinction

Automaton apps must distinguish bundled frontend static assets from runtime media.

```text
Bundled with the app source?
→ put it in the frontend public/static folder

Created after the app is running?
→ put it under AUTOMATON_APP_DATA_DIR
```

Recommended runtime data convention:

```text
$AUTOMATON_APP_DATA_DIR/
  media/
  db/
  generated/
  cache/
  logs/
  tmp/
```

Only `media/` currently has a platform-aware public URL convention.
`AUTOMATON_APP_DATA_DIR` is created and injected by the Automaton runner. Do not hardcode its physical path.

## Frontend static assets

Use the frontend public/static asset folder for files that are part of the app source and only change when the app is edited or redeployed.

Examples:

```text
frontend/public/logo.png
frontend/public/default-cover.jpg
frontend/public/placeholder-avatar.png
frontend/public/favicon.ico
frontend/public/manifest.webmanifest
frontend/public/icons/icon-192.png
frontend/public/sw.js
```

Good uses:

- app logo
- favicon
- default blog cover image
- placeholder avatar
- theme background
- bundled demo images
- PWA manifest, icons, and service worker

These files are built/deployed with the frontend. They are not user data.

For Vite apps, keep references relative where possible so published apps work under `https://apps.automaton.monster/{slug}/`.

Examples:

```html
<img src="./default-cover.jpg" alt="Default cover" />
<link rel="manifest" href="./manifest.webmanifest" />
```

Do not use root-relative app-local URLs such as `/default-cover.jpg`, `/manifest.webmanifest`, or `/icons/icon-192.png`.

## Runtime media and uploads

Use runner app data for files created after the app is running.

Examples:

```text
$AUTOMATON_APP_DATA_DIR/media/uploads/post-cover.jpg
$AUTOMATON_APP_DATA_DIR/media/avatars/user-123.png
$AUTOMATON_APP_DATA_DIR/media/documents/file.pdf
$AUTOMATON_APP_DATA_DIR/generated/report.csv
$AUTOMATON_APP_DATA_DIR/db/app.sqlite3
```

Good uses:

- images uploaded through a blog admin UI
- user avatars uploaded at runtime
- document attachments
- generated reports
- exported files
- SQLite databases and durable app state

Recommended folder meanings:

- `media/` for uploaded images, documents, avatars, and files that may be referenced as `/media/...`
- `db/` for SQLite databases and other durable structured storage
- `generated/` for reports, exports, PDFs, CSVs, and generated documents
- `cache/` for disposable cached data
- `logs/` for optional app-owned log files
- `tmp/` for temporary files safe to delete

Do not write runtime uploads or generated durable files into:

```text
frontend/public/
frontend/dist/
backend/
wwwroot/
live/frontend/
live/backend/
```

Backend/controller files are replaceable deploy output. Frontend public files are bundled static assets. Runtime data belongs under `AUTOMATON_APP_DATA_DIR`.

## Blog app example

A Blog starter may include bundled defaults:

```text
frontend/public/default-cover.jpg
frontend/public/placeholder-author.png
frontend/public/blog-logo.png
```

But images uploaded by the blog owner after the app is running should be stored as runtime media:

```text
$AUTOMATON_APP_DATA_DIR/media/uploads/2026/06/my-post-cover-a1b2c3.jpg
```

Persist app-relative media references in content, not physical runner paths.

Good:

```json
{
  "title": "My first post",
  "coverImage": "/media/uploads/2026/06/my-post-cover-a1b2c3.jpg"
}
```

Bad:

```json
{
  "coverImage": "/home/user/.automaton/apps/app-123/data/media/uploads/cover.jpg"
}
```

## Backend storage pattern

Use `AUTOMATON_APP_DATA_DIR` for durable files:

```python
import os
from pathlib import Path

DATA_DIR = Path(os.environ["AUTOMATON_APP_DATA_DIR"])
MEDIA_DIR = DATA_DIR / "media"
UPLOADS_DIR = MEDIA_DIR / "uploads"
UPLOADS_DIR.mkdir(parents=True, exist_ok=True)
```

For SQLite or other durable structured storage:

```python
DB_DIR = DATA_DIR / "db"
DB_DIR.mkdir(parents=True, exist_ok=True)
DB_PATH = DB_DIR / "app.sqlite3"
```

Generate safe filenames instead of trusting user-provided names.

Example pattern:

```text
media/uploads/{yyyy}/{MM}/{safe-slug}-{random-id}.{extension}
```

## Security rules

When handling uploads:

- validate file size
- validate MIME type and extension
- generate safe filenames
- prevent `../` path traversal
- never expose physical runner paths
- store original filenames only as metadata when needed
- keep media inside the app-scoped data directory
- avoid serving executable files as executable content

## Export/import rule

Runtime media is part of app data and should be portable with the app.

An app export should include the frontend source/build, backend source, app config, database/content if applicable, and app data media.

Do not make ordinary Automaton apps depend on external image hosting when uploaded media should belong to the app owner.
