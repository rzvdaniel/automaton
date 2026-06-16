# Forkable App Templates

## Summary

Automaton templates should be implemented as normal published Automaton apps that can be forked.

The long-term model is:

```text
Build app
  ↓
Publish app
  ↓
Mark as template
  ↓
Users fork it
  ↓
Users customize and deploy their own copy
```

This keeps templates, showcase apps, starter kits, and future marketplace content on the same core app model.

---

## Motivation

Automaton already has the primitives needed for reusable apps:

- apps
- source files
- runtime files
- publishing
- public app pages
- forking
- creators
- categories
- tags

A blog starter, dashboard starter, portfolio starter, or tool starter does not need to be a special CLI scaffold. It can be a complete Automaton app that users fork.

This has an important product advantage:

> Every template is also a real working app.

That means users can open it, inspect it, run it, fork it, and modify it using the same flow as any other Automaton app.

---

## Design Principle

Avoid creating a separate template system when the app model already supports it.

Templates should not be a separate entity type.

Instead, a template is:

```text
A published app with template metadata.
```

Conceptually:

```text
WebApp
├── IsPublished = true
├── IsSample / IsTemplate = true
├── IsFeatured = optional
├── Category = optional
└── Tags = optional
```

This matches the existing app template flow documented in `docs/apps/app-templates.md`.

---

## User Experience

### Web Flow

```text
Automaton Apps
  ↓
Templates / Featured Apps
  ↓
Simple Blog
  ↓
View details
  ↓
Fork
  ↓
Rename app
  ↓
Open editor
  ↓
Deploy / Publish
```

For the official Blog template, the user flow becomes:

```text
Open Simple Blog template
  ↓
Fork
  ↓
Open forked app
  ↓
Deploy / Publish
  ↓
Open /setup
  ↓
Create first admin account
  ↓
Start publishing articles
```

---

## CLI Flow

The CLI should eventually support app-level forking:

```bash
automaton fork simple-blog
```

The command should behave like an Automaton-native equivalent of `git clone`, but for a complete app.

Possible behavior:

1. Resolve `simple-blog` as a published template slug.
2. Create a fork in the current user's account.
3. Download or sync the source locally.
4. Link the local folder to the newly forked app.
5. Optionally run dependency installation for known frontend/backend stacks.
6. Print next steps.

Example output:

```text
Forked Simple Blog as my-simple-blog.
Linked local folder to app 9f2...

Next steps:
  cd my-simple-blog
  automaton deploy
```

---

## Relationship with `automaton new`

`automaton new` should remain focused on creating empty or minimal app scaffolds.

Example:

```bash
automaton new my-app
```

Creates a blank Automaton app structure:

```text
frontend/
backend/
automaton.json
```

`automaton fork` should be used for complete working apps.

Example:

```bash
automaton fork simple-blog
```

Creates a copy of a complete app template.

This separation keeps the mental model simple:

| Command | Purpose |
|---|---|
| `automaton new` | Create a blank/minimal app |
| `automaton fork` | Create from an existing published app/template |
| `automaton deploy` | Deploy current app |
| `automaton publish` | Publish current app |

---

## Why Forking Is Better for Templates

A real template contains much more than folder structure.

For example, the Blog template includes:

- frontend pages
- backend APIs
- database schema
- authentication flow
- setup wizard
- admin UI
- article editor
- theme system
- seed content
- SEO behavior
- RSS feed
- sitemap

This is not just scaffolding.

It is a complete working application.

Therefore, the natural reuse model is fork, not `new`.

---

## Official Templates

Official templates can be created by Automaton admins and marked as featured.

Examples:

- Simple Blog
- Portfolio
- Admin Dashboard
- Internal Tool
- Kanban Board
- Issue Tracker
- CRM Starter
- AI Chat App
- API Backend Starter

Each official template should:

- be a normal Automaton app
- be published
- be marked as template/sample
- include source files
- include a clear README
- be forkable
- avoid hardcoded Automaton-specific secrets
- include first-run setup where needed

---

## Blog Template Example

The Blog template is a good first proof of the model.

It should be implemented as:

```text
Simple Blog
├── frontend: Vue 3 + Vite + TypeScript
├── backend: FastAPI
├── database: SQLite
├── auth: SQLite sessions + HttpOnly cookies
├── setup: /setup first-run wizard
└── customization: settings + themes + layout components
```

The first-run flow should not require environment variables.

```text
No admin exists
  ↓
/admin redirects to /setup
  ↓
User creates blog title, admin email, admin password
  ↓
Admin account and settings are stored in SQLite
  ↓
/setup is disabled
```

This makes the forked app immediately usable after deployment.

---

## Data and Runtime Considerations

When forking a template, Automaton should copy the source needed to edit and redeploy the app.

Runtime artifacts should generally not be copied unless the source is unavailable and the platform intentionally supports runtime-only forks.

Recommended default:

```text
Fork copies source
Fork does not copy runtime version history
Fork clears runner assignment
Fork clears template flags
Fork creates a clean app owned by the user
```

The forked app should start as a normal user app, not as another template.

---

## Marketplace Implications

This model also supports a future marketplace without a new abstraction.

The same app/fork model can power:

- official templates
- community templates
- paid starter kits
- remixable showcase apps
- business app blueprints

Possible future metadata:

```text
IsTemplate
IsOfficial
IsFeatured
Price
License
CreatorId
TemplateCategory
TemplateVersion
```

The core operation remains the same:

```text
Fork app into user's account.
```

---

## Implementation Notes

### Web App

The existing template gallery can remain the primary user-facing path.

The UI should make the distinction clear:

- **Open App**: run the published app
- **Fork**: create your own editable copy
- **Use Template**: user-friendly label for Fork on template cards

### API

Existing fork endpoints can support this model.

A future CLI command can call the same backend operation used by the web UI.

### CLI

Add a future command:

```bash
automaton fork <slug>
```

Optional flags:

```bash
automaton fork simple-blog --name my-company-blog
automaton fork simple-blog --open
automaton fork simple-blog --install
automaton fork simple-blog --deploy
```

Avoid adding many app-specific commands such as:

```bash
automaton new blog
automaton new dashboard
automaton new crm
```

Those should be templates, not hardcoded CLI scaffolds.

---

## Decision

Automaton should treat templates as forkable published apps.

The product direction should be:

```text
Blank app creation: automaton new
Complete app reuse: automaton fork <slug>
Web reuse: Use Template / Fork button
```

This keeps the platform simple, extensible, and aligned with the core Automaton concept:

> Build real apps. Publish them. Fork them. Customize them. Ship instantly.
