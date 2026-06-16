# Automaton Blog Starter

## Summary

Build **Automaton Blog** as a normal Automaton app and use it as both:

1. The real public blog for Automaton.
2. An official forkable starter template for users.

The blog should not be implemented as a special feature in the Automaton website codebase.

It should be a complete Automaton app with frontend, Automaton backend controllers, database, authentication, admin UI, public pages, and deployment behavior.

---

## Product Goal

The blog should prove that Automaton can host real business applications, not only games or demos.

It should demonstrate:

- public hosting
- backend actions
- SQLite persistence
- authentication
- CRUD operations
- admin area
- SEO-friendly pages
- theme customization
- forkable templates
- future custom domain readiness

Core product statement:

> The blog you are reading is built and hosted on Automaton.

---

## Distribution Model

The Blog starter should be distributed primarily through **Fork**, not through a dedicated `automaton new blog` scaffold.

User flow:

```text
Automaton Apps
  ↓
Official Templates
  ↓
Simple Blog
  ↓
Fork
  ↓
Rename app
  ↓
Deploy / Publish
  ↓
Open /setup
  ↓
Create admin account
  ↓
Start publishing
```

Future CLI support may expose this as:

```bash
automaton fork simple-blog
```

This should internally use the same app forking mechanism as the web UI.

Related design document:

```text
docs/design/forkable-app-templates.md
```

---

## Recommended Stack

### Frontend

- Vue 3
- TypeScript
- Vite
- Vue Router
- Plain CSS / CSS variables

### Backend

- Automaton Python backend controllers
- Controller entrypoint contract: `handle(action_name, inputs)`
- No standalone FastAPI service inside the app

### Database

- SQLite

### Storage

- SQLite database for structured content
- Local `/uploads` folder for uploaded images, if supported by the Automaton backend/runtime storage model

---

## Automaton Backend Contract

The blog backend must follow the Automaton app backend contract.

Do **not** implement the blog backend as a standalone FastAPI application.

Backend behavior should be exposed through Python controller modules that implement:

```python
def handle(action_name, inputs):
    ...
```

The frontend should call backend actions through the normal Automaton backend/action mechanism, not directly through custom FastAPI routes.

### Suggested Controller Structure

```text
backend/
  controllers/
    public_blog.py
    admin_blog.py
    auth.py
    setup.py
  lib/
    db.py
    auth_sessions.py
    password_hashing.py
    slugify.py
    seo.py
  data/
    .gitkeep
```

### Suggested Public Actions

```text
public_blog.handle("get_settings", inputs)
public_blog.handle("list_posts", inputs)
public_blog.handle("get_post_by_slug", inputs)
public_blog.handle("list_categories", inputs)
public_blog.handle("list_posts_by_category", inputs)
public_blog.handle("get_rss", inputs)
public_blog.handle("get_sitemap", inputs)
```

### Suggested Setup/Auth Actions

```text
setup.handle("get_setup_status", inputs)
setup.handle("complete_setup", inputs)

auth.handle("login", inputs)
auth.handle("logout", inputs)
auth.handle("get_current_user", inputs)
```

### Suggested Admin Actions

```text
admin_blog.handle("list_posts", inputs)
admin_blog.handle("get_post", inputs)
admin_blog.handle("create_post", inputs)
admin_blog.handle("update_post", inputs)
admin_blog.handle("delete_post", inputs)
admin_blog.handle("publish_post", inputs)
admin_blog.handle("unpublish_post", inputs)
admin_blog.handle("list_categories", inputs)
admin_blog.handle("create_category", inputs)
admin_blog.handle("update_category", inputs)
admin_blog.handle("delete_category", inputs)
admin_blog.handle("get_settings", inputs)
admin_blog.handle("update_settings", inputs)
```

The exact controller filenames and action names can change during implementation, but the backend must remain compatible with Automaton's controller/action runtime.

---

## Authentication Direction

Use session-based authentication, not JWT.

Do not require authentication environment variables.

Do not require SMTP.

Do not require an external identity provider.

### Authentication Principles

- no JWT
- no `BLOG_JWT_SECRET`
- no `BLOG_ADMIN_EMAIL`
- no `BLOG_ADMIN_PASSWORD`
- no email login dependency
- no magic links in V1
- SQLite users table
- SQLite sessions table
- HttpOnly cookies where the Automaton backend/runtime supports cookie handling
- Secure cookies in production when available
- password hashes only

This keeps the starter simple and immediately usable after deployment.

---

## First Run Setup

The application should include a first-run `/setup` wizard.

### No Admin Exists

When the user opens `/admin`:

```text
/admin
  ↓
Check admin user count through setup/get_setup_status action
  ↓
No admin exists
  ↓
Redirect to /setup
```

The setup wizard collects:

- blog title
- admin email
- admin password

The setup wizard creates:

- initial blog settings
- first administrator account
- optional starter categories
- optional sample articles

After setup completes:

```text
/setup disabled permanently
```

### Admin Exists

When an admin already exists:

```text
/setup
  ↓
404 or redirect to /admin/login
```

```text
/admin
  ↓
/admin/login
```

---

## Session Authentication

### Login Flow

```text
Admin email + password
  ↓
auth.handle("login", inputs)
  ↓
Validate credentials
  ↓
Create random session id
  ↓
Store session in SQLite
  ↓
Return/set session cookie depending on Automaton runtime support
```

### Logout Flow

```text
Logout
  ↓
auth.handle("logout", inputs)
  ↓
Delete session from SQLite
  ↓
Clear session cookie / client session reference
```

### Users Table

```sql
Users
-----
Id
Email
PasswordHash
Role
CreatedAt
UpdatedAt
```

### Sessions Table

```sql
Sessions
--------
Id
UserId
ExpiresAt
CreatedAt
LastSeenAt
```

### Password Hashing

Use a strong password hashing algorithm available in the Automaton Python runtime:

- Argon2 preferred if dependency/runtime support is available
- BCrypt acceptable
- PBKDF2 acceptable as a no-extra-dependency fallback

Passwords must never be stored in plain text.

---

## Public Blog Features

V1 public pages:

- homepage
- article list
- article details page
- category pages
- about page

V1 public capabilities:

- published posts only
- clean slugs
- responsive design
- SEO metadata
- OpenGraph metadata
- RSS feed
- sitemap.xml

---

## Admin Features

V1 admin pages:

- login
- dashboard
- posts list
- create post
- edit post
- categories
- settings

V1 admin capabilities:

- create article
- edit article
- delete article
- publish article
- unpublish article
- manage categories
- edit blog title/tagline/footer/social links
- choose theme
- choose homepage layout

---

## Content Model

### Posts

```text
Id
Slug
Title
Excerpt
ContentMarkdown
CoverImageUrl
Status: draft | published
AuthorName
SeoTitle
SeoDescription
CanonicalUrl
PublishedAt
CreatedAt
UpdatedAt
```

### Categories

```text
Id
Slug
Name
Description
CreatedAt
UpdatedAt
```

### Settings

```text
SiteName
Tagline
Description
LogoText
LogoUrl
FaviconUrl
FooterText
Theme
HomeLayout
ArticleLayout
PrimaryColor
AccentColor
ShowAuthor
ShowReadingTime
SocialLinks
CreatedAt
UpdatedAt
```

---

## Theme and Layout Customization

The blog should be intentionally easy to customize.

Customization should work at three levels.

---

### Level 1: Settings-Based Customization

Editable through the Admin UI.

Examples:

- blog title
- tagline
- logo
- favicon
- footer text
- social links
- theme
- homepage layout

This requires no code changes.

---

### Level 2: Theme Customization

Use CSS variables and theme files.

Example:

```css
:root {
  --primary-color: #3b82f6;
  --background-color: #ffffff;
  --text-color: #111827;
  --border-radius: 8px;
}
```

Initial themes:

- default
- minimal
- magazine
- dark

Suggested files:

```text
frontend/src/themes/default.css
frontend/src/themes/minimal.css
frontend/src/themes/magazine.css
frontend/src/themes/dark.css
```

---

### Level 3: Layout Component Customization

Users can modify Vue components directly.

Suggested structure:

```text
frontend/src/
  layouts/
    BlogLayout.vue
    AdminLayout.vue

  components/blog/
    SiteHeader.vue
    SiteFooter.vue
    ArticleCard.vue
    CategoryNav.vue
    MarkdownRenderer.vue

  pages/
    HomePage.vue
    ArticleListPage.vue
    ArticlePage.vue
    CategoryPage.vue
    AboutPage.vue

  pages/admin/
    LoginPage.vue
    SetupPage.vue
    DashboardPage.vue
    PostEditorPage.vue
    CategoriesPage.vue
    SettingsPage.vue

  themes/
    default.css
    minimal.css
    magazine.css
    dark.css
```

Pages should not hardcode headers and footers.

They should use shared layout components:

```vue
<BlogLayout>
  <router-view />
</BlogLayout>
```

This allows users or AI agents to easily change:

- header
- footer
- navigation
- article cards
- homepage structure
- category navigation
- overall visual style

---

## Why Not a Generic Template Engine?

Do not introduce a generic runtime template engine in V1.

Vue components are already the template system.

A separate template engine would add:

- more dependencies
- more complexity
- more security concerns
- more confusing AI-editing behavior

Recommended V1 approach:

```text
Vue components + config + CSS variables
```

Not:

```text
Runtime editable template language
```

---

## SEO Requirements

Each article should support:

- title tag
- meta description
- canonical URL
- OpenGraph title
- OpenGraph description
- OpenGraph image
- published date
- updated date

The app should also provide:

- RSS feed
- sitemap.xml
- clean slugs

Note: a Vite SPA has SEO limits compared to SSR/SSG. This is acceptable for V1, but should be documented.

Future improvements may include:

- static prerendering
- SSR support
- Astro-based blog starter variant

---

## Development Stages

### Stage 1: Core Blog MVP

Goal: a working blog that can publish articles.

Features:

- Automaton Python controllers using `handle(action_name, inputs)`
- SQLite database
- setup wizard
- session authentication
- admin login
- create article
- edit article
- delete article
- publish/unpublish article
- public article list
- public article page

Acceptance criteria:

- backend deploys as an Automaton app backend, not as standalone FastAPI
- first visit to `/admin` redirects to `/setup`
- setup creates the first admin
- admin can log in
- admin can create and publish a post
- published post appears on the public site

---

### Stage 2: Production Blog

Goal: make the blog useful for real Automaton content.

Features:

- categories
- featured articles
- image uploads
- SEO metadata
- OpenGraph metadata
- RSS feed
- sitemap.xml
- about page

Acceptance criteria:

- articles can be categorized
- RSS feed returns published posts through an Automaton backend action
- sitemap includes public article URLs through an Automaton backend action
- article pages expose useful metadata

---

### Stage 3: Theme System

Goal: make the app easy to customize and fork.

Features:

- multiple themes
- theme switcher
- settings-driven theme choice
- layout variants
- clear component boundaries

Acceptance criteria:

- user can change theme from settings
- user can change homepage layout from settings
- header/footer can be customized in one component each
- AI can easily modify visual structure without touching backend

---

### Stage 4: Official Forkable Template

Goal: publish the app as an official Automaton template.

Features:

- publish app
- mark app as template/sample
- mark app as featured
- add template screenshots
- add clear README
- add starter/demo content
- verify fork flow

Acceptance criteria:

- user can open the Blog template from Automaton Apps
- user can fork it
- forked app has source files
- forked app is not marked as a template
- forked app can be deployed and opened
- forked app setup wizard works

---

### Stage 5: Future Enhancements

Possible future features:

- draft preview
- scheduled publishing
- better markdown editor
- multiple authors
- comments
- newsletter integration
- magic-link login
- password reset by email
- analytics dashboard
- custom domains
- static prerendering
- Astro starter variant

---

## Non-Goals for V1

Do not include in V1:

- standalone FastAPI service
- multi-author permissions
- comments
- email login
- password reset emails
- newsletter provider integrations
- full CMS plugin system
- marketplace payments
- custom domains
- multi-framework variants

Keep the first version small, useful, and demo-ready.

---

## Automaton-Specific Notes

This app must behave like any other Automaton app.

It should not require special handling in the platform.

It should use the standard Automaton frontend/backend app structure.

The backend must use Automaton Python controllers with `handle(action_name, inputs)`.

It should be deployable and publishable through the normal app workflow.

The Automaton website should simply link to the published Blog app.

Example future URLs:

```text
apps.automaton.monster/automaton-blog
blog.automaton.monster
```

The stronger message is:

> Automaton's own blog is just another Automaton app.

---

## Success Criteria

The Blog starter is successful when:

1. Automaton uses it as its own blog.
2. Users can fork it as a complete working app.
3. Forked blogs can be set up without environment variables.
4. The admin UI supports normal article publishing.
5. The backend deploys through Automaton's controller/action runtime.
6. The public site looks polished enough for real use.
7. The code is simple enough for AI agents to customize.
8. The template demonstrates the value of Automaton better than another toy demo.

---

## Decision

Build the first official Blog starter as:

```text
Vue 3 frontend
Automaton Python controllers with handle(action_name, inputs)
SQLite persistence
Session auth with secure cookies/session records
First-run /setup wizard
Config-driven settings
CSS theme system
Reusable layout components
Forkable Automaton app template
```

Do not start with:

```text
Standalone FastAPI app
JWT auth
email auth
SMTP setup
custom domain dependency
multi-framework variants
runtime template engine
```
