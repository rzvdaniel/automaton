# App Templates

Admins can publish apps as templates. Users browse templates in a gallery and fork them into their own account with a single click.

---

## Concepts

| Term | Meaning |
|---|---|
| **Template** | A published app marked `IsSample = true` by an admin |
| **Featured** | A template marked `IsFeatured = true` — pinned at the top of the gallery |
| **Duplicate** | A copy of your own app (same user, files included) |
| **Fork** | A copy of a template app into the current user's account |

---

## User Flows

### Browsing Templates (any logged-in user)

1. Navigate to **Apps**.
2. Click the **Templates** tab.
3. Click **Use Template** on any card.
4. The app is forked into your account and you are taken to the editor.

### Duplicating Your Own App

Open the three-dot menu on any app card and choose **Duplicate**.  
A full copy (including all files) is created with the suffix ` (Copy)`.  
Previously, Duplicate created a blank app — this is now fixed.

### Publishing a Template (Admin only)

1. Open the app in the editor and go to **Settings → Templates**.
2. Toggle **Publish as Template** on.
3. Optionally toggle **Featured** to pin the app at the top of the gallery.
4. Click **Save**.

The app must already be published for it to appear in the gallery.

---

## API Reference

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| `GET` | `/api/apps/templates` | Public | Returns all `IsSample && IsPublished` apps |
| `POST` | `/api/apps/{id}/duplicate` | User | Copies the app (files + metadata) for the same user |
| `POST` | `/api/apps/{id}/fork` | User | Copies a template app into the current user's account |
| `PUT` | `/api/apps/{id}/sample` | Admin | Sets `IsSample` and `IsFeatured` flags |

`GET /api/apps/templates` is an alias for the existing `GET /api/apps/samples`. Both endpoints return the same data.

---

## Implementation Notes

### File Copying

App files are stored under separate source/runtime surfaces at `AppData/apps/{userId}/{appId}/`.

`AppFileStorageService.CopyAppFilesAsync(srcUserId, srcAppId, dstUserId, dstAppId)` copies the app `source/` tree to the destination app. Runtime artifacts are not copied. Version snapshots are intentionally excluded so the fork starts clean without inheriting version history.

### Duplicate vs Fork

| | Duplicate | Fork |
|---|---|---|
| Source | Any app you own | Published template only |
| Destination user | Same user | Current (logged-in) user |
| `IsSample` on copy | `false` | `false` |
| Runner inherited | Yes | No (cleared) |
| Name | `{name} (Copy)` | Same as template |

### Database

No migration required. The `IsSample`, `IsFeatured`, `Category`, and `Tags` columns already exist on the `WebApps` table.

---

## Files Changed

| File | Change |
|---|---|
| `Services/AppFileStorageService.cs` | Added `CopyAppFilesAsync` |
| `Services/AppsService.cs` | Added `DuplicateAsync`, `ForkAsync` |
| `Controllers/AppsController.cs` | Added `/templates`, `/{id}/duplicate`, `/{id}/fork` endpoints |
| `features/apps/services/apps.service.ts` | Added `getTemplates`, `duplicate`, `fork`, `setTemplateStatus` |
| `features/apps/stores/apps.store.ts` | Added `duplicateApp`, `forkTemplate` |
| `features/apps/components/AppCard.vue` | Fixed `handleDuplicate` to copy files |
| `features/apps/views/AppListView.vue` | Added Templates tab with gallery |
| `features/apps/components/AppSettings.vue` | Added Templates section (admin only) |
