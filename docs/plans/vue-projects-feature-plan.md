# Projects Feature (Vue.js) Implementation Plan

## Overview
Implement the Projects feature in the Vue.js frontend (Automaton Studio) to allow users to:
- Create, view, and delete projects
- Switch the active project via a global header switcher
- Manage project membership (invite/remove users)
- Manage project assets: assign/remove runners and apps

Scope for this version excludes Flows/Steps integration.

## Goals
- Provide a clear, consistent UI to organize work by project
- Enable collaboration via shared resources (apps, runners) and members
- Maintain an "Active Project" context used across Apps and Runners features

## User Experience
- Header Project Switcher: Top-left dropdown to select the active project (plus "All projects")
- Projects List: List all projects; create, delete, open settings
- Project Settings: Dedicated page with tabs for Members, Runners, Apps

## Architecture Integration
- Feature module at `src/automaton-studio-vue/src/features/projects`
- Use shared Axios `apiClient`, Pinia for state, PrimeVue for UI
- Theme-safe styles per AGENTS.md (avoid hardcoded backgrounds)

## Deliverables (Files)
- `src/features/projects/types/ProjectTypes.ts`
- `src/features/projects/services/projectsService.ts`
- `src/features/projects/stores/projectsStore.ts`
- `src/features/projects/components/ProjectSwitcher.vue`
- `src/features/projects/views/ProjectsListView.vue`
- `src/features/projects/views/ProjectSettingsView.vue`
- `src/features/projects/routes.ts` and router registration

## Data Model (TypeScript)
Match backend DTOs exactly (names/types/optionality). Prefer const objects over enums.
- `Project`: id, name, description?, createdAt, ownerId, memberCount, appCount, runnerCount
- `ProjectSummary`: minimal for list and switcher
- `ProjectMember`: userId, displayName, email, role
- `ProjectRoleConst`: `{ Owner: 'Owner', Admin: 'Admin', Member: 'Member' } as const`
- `ProjectRunnerRef`: runnerId, name, status?
- `ProjectAppRef`: appId, name, published?, publicUrl?

## API Service (projectsService)
Use `apiClient` from shared services. Handle errors with try/catch and toast notifications.
- Projects
  - `listProjects()` → GET `/api/projects`
  - `getProject(projectId)` → GET `/api/projects/{id}`
  - `createProject(payload)` → POST `/api/projects`
  - `updateProject(projectId, payload)` → PUT `/api/projects/{id}`
  - `deleteProject(projectId)` → DELETE `/api/projects/{id}`
- Members
  - `listMembers(projectId)` → GET `/api/projects/{id}/members`
  - `inviteMember(projectId, payload)` → POST `/api/projects/{id}/members`
  - `removeMember(projectId, userId)` → DELETE `/api/projects/{id}/members/{userId}`
- Runners
  - `listRunners(projectId)` → GET `/api/projects/{id}/runners`
  - `addRunner(projectId, runnerId)` → POST `/api/projects/{id}/runners`
  - `removeRunner(projectId, runnerId)` → DELETE `/api/projects/{id}/runners/{runnerId}`
- Apps
  - `listApps(projectId)` → GET `/api/projects/{id}/apps`
  - `addApp(projectId, appId)` → POST `/api/projects/{id}/apps`
  - `removeApp(projectId, appId)` → DELETE `/api/projects/{id}/apps/{appId}`

Note: Endpoint paths are illustrative. Align to existing backend routes once confirmed.

## Pinia Store (projectsStore)
State:
- `projects`: ProjectSummary[]
- `activeProjectId`: string | null
- `members`: ProjectMember[] (for settings view)
- `runners`: ProjectRunnerRef[]
- `apps`: ProjectAppRef[]

Actions:
- `loadProjects()` → populate list for switcher and list view
- `setActiveProject(id | null)` → persist to localStorage and emit event
- `createProject(payload)` / `deleteProject(projectId)` → refresh list
- `loadProjectDetails(projectId)` → members/runners/apps for settings
- `addMember()` / `removeMember()`
- `addRunner()` / `removeRunner()`
- `addApp()` / `removeApp()`

Persistence:
- Keep `activeProjectId` in localStorage; initialize from stored value at app start.

## Header Project Switcher
- Component: `ProjectSwitcher.vue` placed in the global header layout
- UX: PrimeVue `Dropdown` with search; entries include project name and minimal metadata
- Behavior: Changes update `projectsStore.activeProjectId`; emit global event or store subscription to refresh dependent views

## Views
- `ProjectsListView.vue`
  - DataTable list of projects
  - Actions: Create (Dialog + form), Delete (ConfirmDialog), Open Settings
  - Loading states, empty states, toasts
- `ProjectSettingsView.vue`
  - Tabs: Members, Runners, Apps
  - Members: list, invite (email/username), remove; show role badges
  - Runners: list assignable runners owned by user; add/remove
  - Apps: list apps; add/remove from project; show publish status and `publicUrl`

## Cross-Feature Integration
- Apps: Filter [src/features/apps/views/AppListView.vue] by `activeProjectId` via query param or store; show project badge if applicable; allow adding/removing app from project in app details.
- Runners: Filter runner list by `activeProjectId` where applicable; expose assign/remove in project settings only, or provide quick badge.

## Permissions & Roles
- Roles: Owner/Admin/Member
- Capability mapping (frontend):
  - Owner/Admin: can invite/remove members, assign/remove runners/apps, delete project
  - Member: view-only and limited actions (e.g., request access)
- UI: Disable/hide controls when not permitted; display role badges consistently.

## Styling & UX Guidelines
- No hardcoded backgrounds; use `--p-surface-ground`, `--p-text-color`, etc.
- Consistent loading indicators (ref booleans), empty states, toast messages
- Confirm destructive actions (delete project, remove member)

## Testing Strategy
- Unit tests (Vitest): `projectsStore` actions and `projectsService` methods (mock Axios)
- Manual validation: Create project → add members/runners/apps → switch projects → verify Apps/Runners views respect filter
- Validate OpenAPI type generation compatibility if adopted

## Timeline & Milestones
1. Scaffold feature, types, service, store (day 1)
2. Header switcher + ProjectsList (day 2)
3. ProjectSettings tabs (day 3)
4. Apps/Runners integration and permissions (day 4)
5. Tests, polish, docs (day 5)

## Backend Integration (Resolved from ProjectsController)

### Endpoints & Methods
**Projects**
- `GET /api/projects` → list user's projects (returns `ProjectListItem[]`)
- `GET /api/projects/{id}` → get project details (returns `ProjectDto`)
- `POST /api/projects` → create project (request: `CreateProjectRequest`)
- `PUT /api/projects/{id}` → update project (request: `UpdateProjectRequest`; admin only)
- `DELETE /api/projects/{id}` → deactivate project (admin only)

**Members** (route: `/api/projects/{projectId}/members`)
- `GET /api/projects/{projectId}/members` → list members (returns `ProjectMemberDto[]`; any member)
- `GET /api/projects/{projectId}/members/{userId}` → get member details (any member)
- `POST /api/projects/{projectId}/members` → invite/add member (request: `AddProjectMemberRequest`; admin only)
  - Accepts email, auto-creates account if `CreateAccountIfNotExists=true`
- `PUT /api/projects/{projectId}/members/{userId}` → update member role (request: `UpdateProjectMemberRequest`; admin only)
- `DELETE /api/projects/{projectId}/members/{userId}` → remove member (admin only)

**Resources** (route: `/api/projects/{projectId}`)
- Apps:
  - `GET /api/projects/{projectId}/apps` → list project apps
  - `POST /api/projects/{projectId}/apps/{appId}` → link app (developer/admin)
  - `DELETE /api/projects/{projectId}/apps/{appId}` → unlink app (developer/admin)
- Flows:
  - `GET /api/projects/{projectId}/flows` → list project flows
  - `POST /api/projects/{projectId}/flows/{flowId}` → link flow (developer/admin)
  - `DELETE /api/projects/{projectId}/flows/{flowId}` → unlink flow (developer/admin)
- Runners:
  - `GET /api/projects/{projectId}/runners` → list project runners
  - `POST /api/projects/{projectId}/runners/{runnerId}` → link runner (developer/admin)
  - `DELETE /api/projects/{projectId}/runners/{runnerId}` → unlink runner (developer/admin)
- Custom Steps:
  - `GET /api/projects/{projectId}/customsteps` → list project steps
  - `POST /api/projects/{projectId}/customsteps/{stepId}` → link step (developer/admin)
  - `DELETE /api/projects/{projectId}/customsteps/{stepId}` → unlink step (developer/admin)

### DTO Shapes
**ProjectListItem** (for list/switcher)
- id, name, displayName, description?, role, memberCount, updatedAt

**ProjectDto** (for details)
- id, name, displayName, description?, ownerId, ownerName, createdAt, updatedAt, isActive, memberCount, appCount, flowCount, currentUserRole

**ProjectMemberDto**
- projectId, userId, userName, email, role, joinedAt, invitedBy?, invitedByName?, isActive, isOwner

**ProjectRunnerDto** / **ProjectAppDto** / **ProjectFlowDto** / **ProjectCustomStepDto**
- projectId, {runnerId|appId|flowId|stepId}, {runnerName|appName|flowName|stepName}, addedAt, addedBy?, addedByName

### Roles & Permissions
**ProjectRole enum:** `ProjectAdmin`, `Developer`, `AppUser`, `Viewer`

**Capability Matrix:**
| Action | ProjectAdmin | Developer | AppUser | Viewer |
|--------|---|---|---|---|
| View project/members | ✓ | ✓ | ✓ | ✓ |
| Invite/remove members | ✓ | ✗ | ✗ | ✗ |
| Update project settings | ✓ | ✗ | ✗ | ✗ |
| Link/unlink resources (apps/runners/flows/steps) | ✓ | ✓ | ✗ | ✗ |
| View/execute apps | ✓ | ✓ | ✓ | ✗ |
| Read-only access | ✓ | ✓ | ✓ | ✓ |
| Cannot change owner role | ✓ | - | - | - |

Policy names in code: `ProjectMember` (any member), `ProjectAdmin` (admin only), `ProjectResourceManager` (admin + developer).

### Invites & Members
- **Identifier:** Email address
- **Flow:** Auto-join if `CreateAccountIfNotExists=true`; account is auto-created with a temporary password
- **No approval needed:** Member is immediately active
- Member fields: `isActive` flag, `invitedBy` userId, `invitedByName` for audit trail

### Asset Scope & Exclusivity
- Apps/Flows/Runners/Steps can belong to multiple projects or be personal (no strict exclusivity)
- When linked to project, scope becomes `Project` and projectId is set
- When unlinked, scope reverts to `Personal`
- Linking is additive; unlinking removes the project association

### Project Deletion
- Projects are deactivated (soft delete) with `isActive=false`
- Assets become orphaned but retain their data
- Consider UX: show deactivated projects with clear indication; option to reactivate or permanently delete

### "All Projects" Mode
- Implemented via `activeProjectId = null` in store
- Apps/Runners lists show all user's items (not filtered by project) when in "all projects" mode
- Optional: Display project badge on each resource

## Implementation Steps (Sequential)

### Phase 1: Foundation (Types, Service, Store)

**Step 1.1: Create folder structure**
```
src/features/projects/
├── types/
├── services/
├── stores/
├── components/
├── views/
└── routes.ts
```

**Step 1.2: Create `src/features/projects/types/index.ts`**
- Export all type definitions
- Include:
  - `ProjectRole` const object: `{ ProjectAdmin: 'ProjectAdmin', Developer: 'Developer', AppUser: 'AppUser', Viewer: 'Viewer' } as const`
  - `ProjectDto` interface with fields: id, name, displayName, description?, ownerId, ownerName, createdAt, updatedAt, isActive, memberCount, appCount, flowCount, currentUserRole
  - `ProjectListItem` interface with fields: id, name, displayName, description?, role, memberCount, updatedAt
  - `ProjectMemberDto` interface with fields: projectId, userId, userName, email, role, joinedAt, invitedBy?, invitedByName?, isActive, isOwner
  - `ProjectRunnerDto` interface with fields: projectId, runnerId, runnerName, addedAt, addedBy?, addedByName
  - `ProjectAppDto` interface with fields: projectId, appId, appName, addedAt, addedBy?, addedByName
  - `CreateProjectRequest` interface with fields: name, displayName, description?
  - `AddProjectMemberRequest` interface with fields: email, role, createAccountIfNotExists?
  - `UpdateProjectMemberRequest` interface with fields: role
  - `UpdateProjectRequest` interface with fields: displayName, description?, isActive

**Step 1.3: Create `src/features/projects/services/projectsService.ts`**
- Import `apiClient` from `@/shared/services/api/axios-instance`
- Import `useToast` from `primevue/usetoast`
- Implement class `ProjectsService` with methods:
  - `listProjects()` → GET `/api/projects`, return `ProjectListItem[]`
  - `getProject(projectId)` → GET `/api/projects/{projectId}`, return `ProjectDto`
  - `createProject(request)` → POST `/api/projects`, return `ProjectDto`
  - `updateProject(projectId, request)` → PUT `/api/projects/{projectId}`, return `ProjectDto`
  - `deleteProject(projectId)` → DELETE `/api/projects/{projectId}`
  - `listMembers(projectId)` → GET `/api/projects/{projectId}/members`, return `ProjectMemberDto[]`
  - `addMember(projectId, request)` → POST `/api/projects/{projectId}/members`, return `ProjectMemberDto`
  - `updateMemberRole(projectId, userId, request)` → PUT `/api/projects/{projectId}/members/{userId}`, return `ProjectMemberDto`
  - `removeMember(projectId, userId)` → DELETE `/api/projects/{projectId}/members/{userId}`
  - `listRunners(projectId)` → GET `/api/projects/{projectId}/runners`, return `ProjectRunnerDto[]`
  - `addRunner(projectId, runnerId)` → POST `/api/projects/{projectId}/runners/{runnerId}`, return `ProjectRunnerDto`
  - `removeRunner(projectId, runnerId)` → DELETE `/api/projects/{projectId}/runners/{runnerId}`
  - `listApps(projectId)` → GET `/api/projects/{projectId}/apps`, return `ProjectAppDto[]`
  - `addApp(projectId, appId)` → POST `/api/projects/{projectId}/apps/{appId}`, return `ProjectAppDto`
  - `removeApp(projectId, appId)` → DELETE `/api/projects/{projectId}/apps/{appId}`
- All methods: wrap in try/catch, call `useToast().add()` on errors with severity 'error' and summary describing the failure
- Export singleton: `export const projectsService = new ProjectsService()`

**Step 1.4: Create `src/features/projects/stores/projectsStore.ts`**
- Use `defineStore('projects', ...)` from Pinia
- Define state:
  - `projects: ref<ProjectListItem[]>([])`
  - `activeProjectId: ref<string | null>(null)`
  - `members: ref<ProjectMemberDto[]>([])`
  - `runners: ref<ProjectRunnerDto[]>([])`
  - `apps: ref<ProjectAppDto[]>([])`
  - `loading: ref(false)`
- Define computed:
  - `activeProject()`: return project from `projects` matching `activeProjectId`, or null
- Define actions:
  - `loadProjects()`: call service, set state, load from localStorage on init
  - `setActiveProject(id | null)`: update `activeProjectId`, persist to `localStorage.setItem('activeProjectId', ...)`
  - `createProject(request)`: call service, add to `projects` list
  - `deleteProject(projectId)`: call service, remove from `projects` list
  - `loadProjectDetails(projectId)`: call service to load members/runners/apps, update state
  - `addMember(projectId, request)`: call service, add to `members` list
  - `removeMember(projectId, userId)`: call service, remove from `members` list
  - `addRunner(projectId, runnerId)`: call service, add to `runners` list
  - `removeRunner(projectId, runnerId)`: call service, remove from `runners` list
  - `addApp(projectId, appId)`: call service, add to `apps` list
  - `removeApp(projectId, appId)`: call service, remove from `apps` list
- On store init: read `localStorage.getItem('activeProjectId')` and restore if exists

---

### Phase 2: Header Switcher & Projects List

**Step 2.1: Create `src/features/projects/components/ProjectSwitcher.vue`**
- Template:
  - PrimeVue `<Dropdown>` component with:
    - `v-model="store.activeProjectId"`
    - `:options="optionsList"` (projects + "All projects" option)
    - `option-label="displayLabel"`, `option-value="id"`
    - `placeholder="Select a project"`
    - `show-clear` with clear button handler
    - `filter` for search
  - Display current project name or "All projects" in dropdown label
- Script:
  - Import `useProjectsStore`
  - Create computed `optionsList()` combining projects with a special "All projects" item (id: null)
  - Watch dropdown changes and call `store.setActiveProject(id)`
  - Fetch projects on mount with `store.loadProjects()`
  - Show loading state with `store.loading`
- Styling: Use PrimeVue classes, no hardcoded backgrounds

**Step 2.2: Integrate ProjectSwitcher into global header**
- Find the main header component (likely in `src/shared/components/` or `src/App.vue`)
- Add `<ProjectSwitcher />` to the top-left area of the header
- Adjust layout to accommodate the dropdown (typically next to logo or navigation)

**Step 2.3: Create `src/features/projects/views/ProjectsListView.vue`**
- Template:
  - Title: "Projects"
  - Button: "Create Project" (opens dialog)
  - PrimeVue `<DataTable>`:
    - `:value="store.projects"`, `:loading="store.loading"`, `paginator`, `:rows="10"`
    - Columns: name, displayName, description, role (with badge), memberCount, updatedAt (formatted)
    - Row actions: Edit (link to settings), Delete (button → ConfirmDialog)
  - Empty state message if no projects
  - Dialogs:
    - Create Project Dialog with form fields: name, displayName, description
    - Delete ConfirmDialog with confirmation message
- Script:
  - Import `useProjectsStore`, `useRouter`, `useToast`
  - Fetch projects on mount: `store.loadProjects()`
  - On Create: validate form, call `store.createProject()`, close dialog, show success toast
  - On Delete: show ConfirmDialog, call `store.deleteProject()`, show success toast, refresh list
  - On Edit (Settings): navigate to `/projects/{projectId}/settings`
- Styling: theme-safe, no hardcoded backgrounds

---

### Phase 3: Project Settings View

**Step 3.1: Create `src/features/projects/views/ProjectSettingsView.vue`**
- Route params: `projectId` (from URL)
- Template:
  - Title: "Project Settings: {projectName}"
  - Back button to `/projects`
  - Tabs component (PrimeVue `<TabView>`):
    - Tab 1: Members
    - Tab 2: Runners
    - Tab 3: Apps
- Script:
  - Import `useRoute`, `useRouter`, `useProjectsStore`, `useToast`
  - On mount: load project details via `store.loadProjectDetails(projectId)`
  - Expose permission checks via computed (e.g., `canInviteMembers()` based on `currentUserRole`)
  - Initialize tab state

**Step 3.2: Implement Members Tab**
- DataTable:
  - `:value="store.members"`
  - Columns: userName, email, role (with badge), joinedAt, invitedBy, isOwner (icon/label)
  - Actions: Update role (dropdown; disabled if owner), Remove (button → ConfirmDialog)
- Invite Form (in a Card or collapsible section):
  - Fields: email, role (dropdown with ProjectRole options)
  - Button: "Invite" (calls `store.addMember()`)
  - Success toast after invite, clear form
  - Disable if not ProjectAdmin role
- Role Badge styling: color-coded (admin=red, developer=yellow, appuser=blue, viewer=gray)
- Owner row: highlight with special badge, no actions

**Step 3.3: Implement Runners Tab**
- DataTable:
  - `:value="store.runners"`
  - Columns: runnerName, addedAt, addedByName
  - Actions: Remove (button → ConfirmDialog; only if ProjectAdmin or Developer)
- Add Runners Card:
  - Dropdown or MultiSelect of available runners (not already in project)
  - Filter to only runners owned by current user or org runners (per backend logic)
  - Button: "Add Runner" → calls `store.addRunner()`
  - Success toast, refresh list
  - Disable if not Developer/ProjectAdmin role
- Empty state: "No runners assigned to this project"

**Step 3.4: Implement Apps Tab**
- DataTable:
  - `:value="store.apps"`
  - Columns: appName, addedAt, addedByName
  - Actions: Remove (button → ConfirmDialog; only if ProjectAdmin or Developer)
- Add Apps Card:
  - Dropdown or search/select of available apps (not already in project)
  - Button: "Add App" → calls `store.addApp()`
  - Success toast, refresh list
  - Disable if not Developer/ProjectAdmin role
- Empty state: "No apps assigned to this project"

---

### Phase 4: Routes & Permissions

**Step 4.1: Create `src/features/projects/routes.ts`**
- Define routes:
  ```ts
  {
    path: '/projects',
    component: ProjectsListView,
    meta: { requiresAuth: true, title: 'Projects' }
  },
  {
    path: '/projects/:id/settings',
    component: ProjectSettingsView,
    meta: { requiresAuth: true, title: 'Project Settings' }
  }
  ```

**Step 4.2: Register routes in global router**
- Find `src/router/index.ts` or `src/router.ts`
- Import projects routes
- Add to main routes array

**Step 4.3: Implement permission checks in views**
- ProjectSettingsView:
  - Compute `canManageMembers()` → `currentUserRole === 'ProjectAdmin'`
  - Compute `canManageResources()` → `['ProjectAdmin', 'Developer'].includes(currentUserRole)`
  - Disable/hide buttons and forms based on these checks
  - Show toast "Unauthorized" if user tries to access without permission (backend will reject anyway)

**Step 4.4: Add role badge utility function**
- Create `src/features/projects/utils/roleUtils.ts`:
  - Function `getRoleColor(role)`: return color for role badge (ProjectAdmin='red', Developer='yellow', AppUser='blue', Viewer='gray')
  - Function `getRoleLabel(role)`: return human-readable label
  - Use in DataTable columns via custom templates

---

### Phase 5: Apps/Runners Feature Integration

**Step 5.1: Update AppListView**
- Import `useProjectsStore`
- Add computed `filteredApps()`: filter apps by `activeProjectId` if not null, otherwise all
- Add column/badge: project name (if applicable)
- Update API call to include query param: `?projectId={activeProjectId}` if filtering
- Optional: Add context menu to add/remove app from project

**Step 5.2: Update RunnerListView**
- Similar to AppListView: filter by `activeProjectId`, add project badge, context menu

**Step 5.3: (Optional) Add quick action to add apps/runners to project**
- In AppListView/RunnerListView detail view or context menu
- Button: "Add to project" → shows dialog to select project (from store.projects)
- Call `store.addApp()` or `store.addRunner()`

---

### Phase 6: Polish & Documentation

**Step 6.1: Add loading states & empty states**
- ProjectsListView: Empty state message when `projects.length === 0`
- ProjectSettingsView tabs: Empty state for each tab if no data
- Use PrimeVue `<DataTable>` built-in empty state or custom message component

**Step 6.2: Add confirmation dialogs for destructive actions**
- Delete project: "Are you sure? This will deactivate the project."
- Remove member: "Are you sure? This user will no longer have access."
- Remove runner/app: "Are you sure? This resource will be unlinked from the project."
- Use PrimeVue `<ConfirmDialog>` component

**Step 6.3: Implement theme-safe styling**
- Audit all CSS in .vue files for hardcoded colors/backgrounds
- Replace with CSS variables:
  - Backgrounds: `var(--p-surface-ground)` or omit (inherit)
  - Text: `var(--p-text-color)`
  - Borders: `var(--p-surface-border)` or `var(--p-surface-200)`
- Test in light and dark modes

**Step 6.4: Update AGENTS.md**
- Add Projects feature description to feature list
- Document ProjectRole capabilities (table)
- Add note on activeProjectId persistence and header switcher behavior
- Include code example of filtering list by activeProjectId

**Step 6.5: Add JSDoc comments**
- Document all public methods in service and store
- Include param/return types and descriptions

---

### Phase 7: Testing (Optional v1)

**Step 7.1: Unit test projectsStore**
- Mock `projectsService` calls
- Test `loadProjects()`: verify state update and localStorage persistence
- Test `setActiveProject()`: verify state update and localStorage write
- Test `createProject()`: verify list update
- Test `deleteProject()`: verify removal from list

**Step 7.2: Unit test projectsService**
- Mock `apiClient`
- Test each method returns correct type and handles errors
- Test error toast is called on failure

**Step 7.3: Manual validation**
- Create a test project via UI
- Invite a test user with email
- Add a runner to the project
- Add an app to the project
- Switch projects in header switcher
- Verify AppListView/RunnerListView filters reflect active project
- Verify all toasts appear as expected
