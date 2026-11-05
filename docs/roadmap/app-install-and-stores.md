# App install and shared stores

Updated: 3 November 2026. Status: planned. Work and status: parent issue [#551](https://github.com/rzvdaniel/automaton/issues/551) and its sub-issues; this file holds the design.

Anyone on an Automaton server can find a useful app in a store, on their own server or on another one, and install it from Studio without an AI, the CLI or writing code: fill in its settings and secrets, approve its permissions, deploy. Any published app can be installed this way; there is no separate template concept. This is the next phase of [App store](app-store.md) and what [Self-maintenance](self-maintenance.md) uses to deliver maintenance apps.

Listed in the [roadmap index](README.md).

## Design

### Today

- Every server has a public store ([App store](app-store.md)). Fork copies a published app's source and build/runtime settings to a new app on the **same** server (`AppForkingService`), clearing environment values.
- Deploy only accepts a package uploaded by the CLI (`POST api/apps/{id}/deploy-package`; the package is built by the CLI's `ZipService`). The server keeps the source of each successful deploy, but cannot deploy it again by itself, so a forked app only runs after someone deploys it with the CLI.
- The Runner already builds from the package source with the app's build command. Nothing on the Runner changes in this plan.

### Install flow

1. **Browse** in Studio → App Store. A store selector shows *This server*, *automaton.monster* (preset) and stores an administrator added by URL. The app page shows description, source, versions and what the app needs: settings, secrets, permissions, default schedules.
2. **Install** = fork. The server copies the published version's source (from a remote store: downloads the version's package) into a new app owned by the user, and records its origin.
3. **Set up.** Studio asks for the settings and secrets the app declares, shows requested permissions for approval (Self-maintenance step 2; infrastructure permissions need a system administrator) and offers its default schedules.
4. **Deploy** from the stored source through the existing durable deployment pipeline; the Runner builds and starts it.

The installed app is an ordinary app: editable, with its own runs, logs and schedules. It does not depend on the origin store after install, and the origin never sees its settings or secrets.

### Rules

- **Nothing installs or updates on its own.** A person explicitly installs a pinned published version; an update from origin is shown with its diff and applied only when the owner chooses. This replaces Self-maintenance's earlier rule that servers never download app code at runtime (decided 3 November 2026).
- Remote packages come over HTTPS from a store an administrator added, are pinned to an exact version and checksum, and are recorded with their origin (store, app, version) in the audit log.
- An installed app has only the permissions it declares and someone approved. No special execution path for store apps.
- Fork exclusions stay: secret-like files, runtime state and environment values are never copied.

### Not in this plan

A central templates service, catalog federation or syncing between stores, search across stores, ratings, paid apps, signed packages or verified publishers (possible later for automaton.monster), and reusing the origin's build output instead of building.

## Plan

Each step depends on the previous one; step 5 depends on steps 2–3.

1. **Deploy from stored source** (#552) — implemented. Move package creation from the CLI's `ZipService` into a library shared by CLI and API; add `POST api/apps/{id}/deploy` that packages the app's stored source and hands it to `DeploymentOperationService` (same authorization as `deploy-package`); add a Deploy button to the app page. *Acceptance:* an app forked on the same server is deployed and runs without the CLI.
2. **Install manifest** (#553). `automaton.json` declares settings, secrets (`application.secrets`), permissions and default schedules for installers; fork on the same server runs the set-up flow above and deploys. *Acceptance:* a user installs a published app with secrets, permissions and a schedule from Studio alone and its scheduled Action runs.
3. **Remote stores** (#554). Public store listing and version package download for forkable apps; Admin → Stores to add a store by URL (automaton.monster preset); install from a remote store with origin recorded. *Acceptance:* an app published on one server is installed and runs on another.
4. **Update from origin** (#555). Studio shows "Update available" for installed apps whose origin has a newer version, with a source diff; applying it keeps settings, secrets, approvals and schedules. *Acceptance:* an update is applied on request, and an edited app's changes are shown before they are replaced.
5. **Admin → Maintenance** (#485). The store view of maintenance apps from [Self-maintenance](self-maintenance.md#delivery): the store filtered to the `maintenance` category, with installed version and "Update available"; Studio suggests relevant ones once a Runner is online. *Acceptance:* an administrator installs a maintenance app from Admin → Maintenance, fills its secrets, approves its permissions, and its scheduled Action runs.
