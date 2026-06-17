# User Public Apps Page

## Overview

A public-facing page where anonymous visitors can browse a user's published apps. Accessible at `automaton.monster/{username}/apps` without authentication.

**Goal:** Transform Automaton into a platform where users showcase their creations, driving organic growth and laying groundwork for future monetization.

---

## Feature Summary

| Aspect | Description |
|--------|-------------|
| Route | `/{username}/apps` |
| Auth | Anonymous (no login required) |
| Content | User's public apps in Netflix-style card grid |
| Actions | View details, open app in new tab |

---

## Implementation Plan

### Phase 1: Backend API

#### 1.1 Create Public Apps Controller

**File:** `src/Automaton.Api/Controllers/PublicAppsController.cs`

```csharp
[Route("api/public")]
[ApiController]
public class PublicAppsController : ControllerBase
{
    // GET /api/public/{username}/apps
    // Returns public apps for a user (no auth required)

    // GET /api/public/{username}/apps/{appId}
    // Returns single public app details (no auth required)

    // GET /api/public/{username}/profile
    // Returns public profile info (username, avatar, bio)
}
```

**Endpoints:**

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/public/{username}/apps` | List user's public apps |
| GET | `/api/public/{username}/apps/{appId}` | Get single app details |
| GET | `/api/public/{username}/profile` | Get public profile info |

#### 1.2 DTOs

```csharp
public class PublicUserProfileDto
{
    public string Username { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public int PublicAppCount { get; set; }
}

public class PublicAppCardDto
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Category { get; set; }
    public DateTime PublishedAt { get; set; }
    public string AppUrl { get; set; }  // Direct link to open app
}

public class PublicAppDetailsDto : PublicAppCardDto
{
    public string? LongDescription { get; set; }
    public List<string>? Screenshots { get; set; }
    public List<string>? Tags { get; set; }
}
```

#### 1.3 Database Changes

**Add to App entity:**

```csharp
public bool IsPublic { get; set; } = false;
public DateTime? PublishedAt { get; set; }
public string? ThumbnailUrl { get; set; }
public string? Category { get; set; }
public string? LongDescription { get; set; }
```

**Add to User entity (if not exists):**

```csharp
public string? Bio { get; set; }
public string? AvatarUrl { get; set; }
public string? DisplayName { get; set; }
```

**Migration:** `AddPublicAppFields`

#### 1.4 Service Layer

**File:** `src/Automaton.Api/Services/PublicAppsService.cs`

```csharp
public class PublicAppsService
{
    Task<PublicUserProfileDto?> GetPublicProfile(string username);
    Task<List<PublicAppCardDto>> GetPublicApps(string username);
    Task<PublicAppDetailsDto?> GetPublicAppDetails(string username, Guid appId);
}
```

---

### Phase 2: Frontend - Public Apps Page

#### 2.1 Feature Structure

```
src/automaton-studio-vue/src/features/public/
├── components/
│   ├── PublicAppCard.vue         # Netflix-style app card
│   ├── PublicAppGrid.vue         # Grid layout for cards
│   ├── PublicAppDetails.vue      # Modal with full details
│   └── PublicUserHeader.vue      # User profile header
├── services/
│   └── publicApps.service.ts     # API calls (no auth)
├── types/
│   └── publicApps.types.ts       # TypeScript interfaces
├── views/
│   └── PublicAppsView.vue        # Main page component
└── routes.ts                     # Route definition
```

#### 2.2 Route Configuration

```typescript
// No auth guard - public route
{
  path: '/:username/apps',
  name: 'public-apps',
  component: () => import('./views/PublicAppsView.vue'),
  meta: {
    public: true,
    layout: 'public'  // Minimal layout without sidebar
  }
}
```

#### 2.3 Components

**PublicAppsView.vue** - Main page layout:
- PublicUserHeader (avatar, name, bio)
- PublicAppGrid (Netflix-style card grid)
- Empty state if no public apps
- Footer with CTA: "Create your own apps on Automaton"

**PublicAppCard.vue** - Individual app card:
- Thumbnail image (placeholder if none)
- App name
- Short description (truncated)
- Hover effect with "Open" and "Details" buttons

**PublicAppDetails.vue** - Modal dialog:
- Full description
- Screenshots carousel (if available)
- Tags
- "Open App" button
- Share link

**PublicUserHeader.vue** - Profile section:
- Avatar
- Display name / username
- Bio
- App count

#### 2.4 Styling

- Netflix-style horizontal scrolling rows OR responsive grid
- Dark theme friendly (use CSS variables)
- Card hover animations
- Responsive: 1 col mobile, 2-3 tablet, 4-5 desktop

---

### Phase 3: App Publishing Flow

#### 3.1 Add Visibility Toggle to App Settings

**Location:** App editor settings panel

```vue
<div class="visibility-setting">
  <label>App Visibility</label>
  <SelectButton
    v-model="app.isPublic"
    :options="[
      { label: 'Private', value: false },
      { label: 'Public', value: true }
    ]"
  />
  <small>Public apps appear on your profile page</small>
</div>
```

#### 3.2 Publishing Metadata

When setting app to public, prompt for:
- Thumbnail (upload or auto-generate)
- Category (dropdown)
- Tags (multi-select)
- Long description (optional)

---

### Phase 4: Public Layout

#### 4.1 Create Minimal Public Layout

**File:** `src/automaton-studio-vue/src/layouts/PublicLayout.vue`

- Minimal header (Automaton logo only)
- No sidebar
- No auth-required navigation
- Footer with links: Home, Sign Up, Login

---

## Database Schema Changes

```sql
-- Migration: AddPublicAppFields

ALTER TABLE Apps ADD COLUMN IsPublic BOOLEAN DEFAULT FALSE;
ALTER TABLE Apps ADD COLUMN PublishedAt DATETIME NULL;
ALTER TABLE Apps ADD COLUMN ThumbnailUrl VARCHAR(500) NULL;
ALTER TABLE Apps ADD COLUMN Category VARCHAR(100) NULL;
ALTER TABLE Apps ADD COLUMN LongDescription TEXT NULL;

ALTER TABLE AspNetUsers ADD COLUMN Bio VARCHAR(500) NULL;
ALTER TABLE AspNetUsers ADD COLUMN AvatarUrl VARCHAR(500) NULL;
ALTER TABLE AspNetUsers ADD COLUMN DisplayName VARCHAR(100) NULL;

-- Index for efficient public apps queries
CREATE INDEX IX_Apps_UserId_IsPublic ON Apps(UserId, IsPublic) WHERE IsPublic = 1;
```

---

## API Specification

### GET /api/public/{username}/apps

**Response 200:**
```json
{
  "profile": {
    "username": "johndoe",
    "displayName": "John Doe",
    "avatarUrl": "https://...",
    "bio": "Building cool automations",
    "publicAppCount": 5
  },
  "apps": [
    {
      "id": "guid",
      "name": "Invoice Generator",
      "description": "Generate professional invoices",
      "thumbnailUrl": "https://...",
      "category": "Business",
      "publishedAt": "2026-01-15T...",
      "appUrl": "https://automaton.monster/apps/guid"
    }
  ]
}
```

**Response 404:** User not found

### GET /api/public/{username}/apps/{appId}

**Response 200:**
```json
{
  "id": "guid",
  "name": "Invoice Generator",
  "description": "Generate professional invoices",
  "longDescription": "Full markdown description...",
  "thumbnailUrl": "https://...",
  "screenshots": ["url1", "url2"],
  "tags": ["business", "pdf", "automation"],
  "category": "Business",
  "publishedAt": "2026-01-15T...",
  "appUrl": "https://automaton.monster/apps/guid"
}
```

**Response 404:** App not found or not public

---

## Security Considerations

1. **Rate Limiting** - Apply rate limits to public endpoints
2. **Data Exposure** - Only expose safe fields (no internal IDs, no user emails)
3. **Username Validation** - Sanitize username in routes
4. **App Content** - Consider content moderation for public apps
5. **CORS** - Configure appropriate CORS for public routes

---

## SEO & Metadata

### Dynamic Meta Tags

```html
<title>{displayName}'s Apps - Automaton</title>
<meta name="description" content="Explore {displayName}'s automation apps on Automaton">
<meta property="og:title" content="{displayName}'s Apps">
<meta property="og:image" content="{avatarUrl or default}">
```

### Sitemap

Add public profile pages to sitemap for indexing.

---

## Future Enhancements

1. **App Ratings/Reviews** - Users rate public apps
2. **Featured Apps** - Curated homepage showcase
3. **Categories Page** - Browse all public apps by category
4. **Search** - Search across all public apps
5. **Monetization** - Paid apps, subscriptions, donations
6. **Analytics** - View counts, popular apps dashboard
7. **Embedding** - Embed app cards on external sites
8. **Followers** - Follow users to see new apps

---

## Implementation Checklist

### Backend
- [ ] Create `PublicAppsController`
- [ ] Create `PublicAppsService`
- [ ] Add DTOs
- [ ] Create database migration
- [ ] Add rate limiting middleware
- [ ] Write unit tests

### Frontend
- [ ] Create `public` feature folder structure
- [ ] Implement `PublicAppsView.vue`
- [ ] Implement `PublicAppCard.vue`
- [ ] Implement `PublicAppGrid.vue`
- [ ] Implement `PublicAppDetails.vue`
- [ ] Implement `PublicUserHeader.vue`
- [ ] Create `PublicLayout.vue`
- [ ] Add public route
- [ ] Implement API service
- [ ] Add visibility toggle to app settings
- [ ] Style for light/dark themes
- [ ] Test responsive design

### Infrastructure
- [ ] Configure route for `/{username}/apps`
- [ ] Set up SEO meta tags
- [ ] Configure caching for public pages
- [ ] Add to sitemap

---

## Notes

- Start with basic card grid, iterate on Netflix-style later
- Thumbnails can be auto-generated from app preview initially
- Categories should be predefined (not user-created) for consistency
