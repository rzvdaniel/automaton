# Project/Organization Feature - Solo Implementation Plan

## Overview

This document provides a stage-by-stage implementation plan for the Project/Organization multi-tenancy feature. Each stage is designed to be implemented, tested, and reviewed independently before moving to the next stage.

**Target:** Solo developer implementation with incremental review checkpoints  
**Focus:** App-centric user experience (no FlowUser role)  
**Roles:** ProjectAdmin, Developer, AppUser, Viewer (4 roles)

---

## Stage 1: Database Foundation (Week 1)

### Goal
Create the core database schema and entity models without breaking existing functionality.

### Tasks

#### 1.1 Create New Entity Models
**Files to create:**
- `src/Automaton.Studio.Server/Entities/Project.cs`
- `src/Automaton.Studio.Server/Entities/ProjectMember.cs`
- `src/Automaton.Studio.Server/Entities/ProjectApp.cs`
- `src/Automaton.Studio.Server/Entities/ProjectFlow.cs`
- `src/Automaton.Studio.Server/Entities/ProjectRunner.cs`
- `src/Automaton.Studio.Server/Entities/ProjectCustomStep.cs`

#### 1.2 Create Enums
**Files to create:**
- `src/Automaton.Studio.Server/Enums/ProjectRole.cs` (4 roles: ProjectAdmin, Developer, AppUser, Viewer)
- `src/Automaton.Studio.Server/Enums/AccessScope.cs` (Personal, Project, Public)

#### 1.3 Update ApplicationDbContext
**File to modify:**
- `src/Automaton.Studio.Server/Data/ApplicationDbContext.cs`
  - Add DbSets for new entities
  - Configure entity relationships in OnModelCreating
  - Add indexes for performance

#### 1.4 Update ApplicationUser
**File to modify:**
- `src/Automaton.Studio.Server/Entities/ApplicationUser.cs`
  - Add `IsEnterpriseEnabled` flag
  - Add `CurrentProjectId` (optional)
  - Add navigation properties: `OwnedProjects`, `ProjectMemberships`

#### 1.5 Update WebApp Entity
**File to modify:**
- `src/Automaton.Studio.Server/Entities/WebApp.cs`
  - Add `Scope` property (AccessScope enum)
  - Add `ProjectId` (nullable)
  - Add `ProjectApps` navigation property

#### 1.6 Create and Apply Migration
```bash
cd src/Automaton.Studio.Server
dotnet ef migrations add AddProjectEntities
dotnet ef database update
```

### Validation
- [ ] Migration runs successfully
- [ ] All existing tests pass
- [ ] No breaking changes to existing functionality
- [ ] Database schema matches design

### Review Checkpoint
Commit and create PR for review. Wait for approval before proceeding.

---

## Stage 2: Core Services Layer (Week 1-2)

### Goal
Implement business logic services for project management without exposing through APIs yet.

### Tasks

#### 2.1 Create ProjectService
**File to create:**
- `src/Automaton.Studio.Server/Application/Services/ProjectService.cs`

**Methods:**
- `CreateProjectAsync(ownerId, name, displayName, description)`
- `GetUserProjectsAsync(userId)`
- `GetProjectDetailAsync(projectId)`
- `UpdateProjectAsync(projectId, request)`
- `DeleteProjectAsync(projectId)`

#### 2.2 Create ProjectMemberService
**File to create:**
- `src/Automaton.Studio.Server/Application/Services/ProjectMemberService.cs`

**Methods:**
- `GetMemberAsync(projectId, userId)`
- `AddMemberAsync(projectId, userId, role, invitedBy)`
- `UpdateMemberRoleAsync(projectId, userId, newRole)`
- `RemoveMemberAsync(projectId, userId)`
- `GetProjectMembersAsync(projectId)`
- `ValidateRoleChange(currentRole, targetRole)` - prevents removing last admin

#### 2.3 Create ProjectAuthorizationService
**File to create:**
- `src/Automaton.Studio.Server/Application/Services/ProjectAuthorizationService.cs`

**Methods:**
- `CanAccessProject(userId, projectId)`
- `HasRole(userId, projectId, allowedRoles)`
- `IsProjectAdmin(userId, projectId)`
- `CanCreateResources(userId, projectId)`
- `CanAccessApp(userId, appId)`

#### 2.4 Create ProjectResourceService
**File to create:**
- `src/Automaton.Studio.Server/Application/Services/ProjectResourceService.cs`

**Methods:**
- `LinkAppToProject(projectId, appId, addedBy)`
- `UnlinkAppFromProject(projectId, appId)`
- `GetProjectApps(projectId, userId)` - filtered by role
- Similar methods for Flows, Runners, CustomSteps

#### 2.5 Register Services
**File to modify:**
- `src/Automaton.Studio.Server/Program.cs`

```csharp
services.AddScoped<ProjectService>();
services.AddScoped<ProjectMemberService>();
services.AddScoped<ProjectAuthorizationService>();
services.AddScoped<ProjectResourceService>();
```

### Unit Tests
**Files to create:**
- `src/UnitTests/Automaton.Studio.Server.Tests/Services/ProjectServiceTests.cs`
- `src/UnitTests/Automaton.Studio.Server.Tests/Services/ProjectMemberServiceTests.cs`
- `src/UnitTests/Automaton.Studio.Server.Tests/Services/ProjectAuthorizationServiceTests.cs`

**Test scenarios:**
- Create project and verify owner is added as ProjectAdmin
- Add member with each role
- Prevent removing last ProjectAdmin
- Authorization checks for each role
- Resource access filtering by role

### Validation
- [ ] All unit tests pass
- [ ] Service methods work correctly in isolation
- [ ] Authorization logic validates correctly
- [ ] Edge cases handled (last admin, invalid roles, etc.)

### Review Checkpoint
Commit and create PR for review. Wait for approval before proceeding.

---

## Stage 3: Authorization Policies (Week 2)

### Goal
Implement ASP.NET Core authorization policies for project-based access control.

### Tasks

#### 3.1 Create Authorization Requirements
**File to create:**
- `src/Automaton.Studio.Server/Authorization/ProjectPolicyRequirements.cs`

```csharp
public class ProjectMemberRequirement : IAuthorizationRequirement
{
    public string[] AllowedRoles { get; }
}

public class ProjectMemberHandler : AuthorizationHandler<ProjectMemberRequirement>
{
    // Implementation validates user is project member with allowed role
}
```

#### 3.2 Register Authorization Policies
**File to modify:**
- `src/Automaton.Studio.Server/Program.cs`

```csharp
services.AddAuthorization(options =>
{
    options.AddPolicy("ProjectAdmin", policy =>
        policy.Requirements.Add(new ProjectMemberRequirement(ProjectRole.ProjectAdmin)));
    
    options.AddPolicy("ProjectDeveloper", policy =>
        policy.Requirements.Add(new ProjectMemberRequirement(
            ProjectRole.ProjectAdmin, ProjectRole.Developer)));
    
    options.AddPolicy("ProjectAppUser", policy =>
        policy.Requirements.Add(new ProjectMemberRequirement(
            ProjectRole.ProjectAdmin, ProjectRole.Developer, ProjectRole.AppUser)));
});

services.AddScoped<IAuthorizationHandler, ProjectMemberHandler>();
```

### Validation
- [ ] Authorization policies registered correctly
- [ ] Policy handlers execute without errors
- [ ] Policy evaluation matches expected behavior

### Review Checkpoint
Commit and create PR for review. Wait for approval before proceeding.

---

## Stage 4: API Controllers (Week 2-3)

### Goal
Expose project management functionality through REST API endpoints.

### Tasks

#### 4.1 Create Models/DTOs
**Files to create:**
- `src/Automaton.Studio.Server/Models/ProjectModels.cs`
  - `ProjectDto`
  - `CreateProjectRequest`
  - `UpdateProjectRequest`
- `src/Automaton.Studio.Server/Models/ProjectMemberModels.cs`
  - `ProjectMemberDto`
  - `AddMemberRequest`
  - `UpdateMemberRoleRequest`

#### 4.2 Create ProjectsController
**File to create:**
- `src/Automaton.Studio.Server/Controllers/ProjectsController.cs`

**Endpoints:**
- `GET /api/projects` - List user's projects
- `POST /api/projects` - Create new project
- `GET /api/projects/{id}` - Get project details
- `PUT /api/projects/{id}` - Update project
- `DELETE /api/projects/{id}` - Delete project (ProjectAdmin only)

#### 4.3 Create ProjectMembersController
**File to create:**
- `src/Automaton.Studio.Server/Controllers/ProjectMembersController.cs`

**Endpoints:**
- `GET /api/projects/{id}/members` - List members
- `POST /api/projects/{id}/members` - Add member (creates account if needed)
- `PUT /api/projects/{id}/members/{userId}` - Update member role
- `DELETE /api/projects/{id}/members/{userId}` - Remove member

#### 4.4 Create ProjectResourcesController
**File to create:**
- `src/Automaton.Studio.Server/Controllers/ProjectResourcesController.cs`

**Endpoints:**
- `POST /api/projects/{id}/apps/{appId}` - Link app to project
- `DELETE /api/projects/{id}/apps/{appId}` - Unlink app
- `GET /api/projects/{id}/apps` - List project apps (filtered by role)
- Similar endpoints for flows, runners, steps

#### 4.5 Apply Authorization Attributes
```csharp
[Authorize(Policy = "ProjectAdmin")]
[HttpPost("api/projects/{projectId}/members")]
public async Task<IActionResult> AddMember(...)

[Authorize(Policy = "ProjectDeveloper")]
[HttpPost("api/projects/{projectId}/apps")]
public async Task<IActionResult> LinkApp(...)
```

### Integration Tests
**Files to create:**
- `src/UnitTests/Automaton.Studio.Server.Tests/Controllers/ProjectsControllerTests.cs`
- Test each endpoint with different roles
- Test authorization (403 Forbidden for unauthorized)
- Test validation (400 Bad Request for invalid input)

### Validation
- [ ] All endpoints return correct responses
- [ ] Authorization works correctly
- [ ] Non-members get 403 Forbidden
- [ ] Invalid input returns 400 Bad Request
- [ ] Swagger documentation generated correctly

### Review Checkpoint
Commit and create PR for review. Test APIs with Postman/Swagger. Wait for approval before proceeding.

---

## Stage 5: Client-Side Services (Week 3)

### Goal
Create Blazor services to consume the new APIs from the UI.

### Tasks

#### 5.1 Create Client Models
**Files to create:**
- `src/Automaton.Studio/Models/ProjectDto.cs`
- `src/Automaton.Studio/Models/ProjectMemberDto.cs`
- `src/Automaton.Studio/Models/CreateProjectRequest.cs`
- `src/Automaton.Studio/Models/AddMemberRequest.cs`

#### 5.2 Create ProjectService (Client)
**File to create:**
- `src/Automaton.Studio/Services/ProjectService.cs`

**Methods:**
- `GetProjectsAsync()`
- `GetProjectAsync(id)`
- `CreateProjectAsync(request)`
- `UpdateProjectAsync(id, request)`
- `DeleteProjectAsync(id)`

#### 5.3 Create ProjectMemberService (Client)
**File to create:**
- `src/Automaton.Studio/Services/ProjectMemberService.cs`

**Methods:**
- `GetMembersAsync(projectId)`
- `AddMemberAsync(projectId, request)`
- `UpdateRoleAsync(projectId, userId, newRole)`
- `RemoveMemberAsync(projectId, userId)`

#### 5.4 Create ProjectContextService
**File to create:**
- `src/Automaton.Studio/Services/ProjectContextService.cs`

**Purpose:** Track current project context for the user session

**Methods:**
- `GetCurrentProjectAsync()`
- `SetCurrentProjectAsync(projectId)`
- `GetUserRoleInProjectAsync(projectId)`

#### 5.5 Register Services
**File to modify:**
- `src/Automaton.Studio/Program.cs`

```csharp
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<ProjectMemberService>();
builder.Services.AddScoped<ProjectContextService>();
```

### Validation
- [ ] Services successfully call APIs
- [ ] Data serialization/deserialization works
- [ ] Error handling implemented
- [ ] Services registered correctly

### Review Checkpoint
Commit and create PR for review. Wait for approval before proceeding.

---

## Stage 6: Projects List Page (Week 3-4)

### Goal
Create the main Projects page where users can view and manage their projects.

### Tasks

#### 6.1 Create Projects Page
**Files to create:**
- `src/Automaton.Studio/Pages/Projects/ProjectsPage.razor`
- `src/Automaton.Studio/Pages/Projects/ProjectsPage.razor.cs`
- `src/Automaton.Studio/Pages/Projects/ProjectsPage.razor.css`

**Features:**
- Grid/card view of user's projects
- "Create Project" button
- Project cards showing:
  - Name and description
  - Member count
  - Role badge (Admin, Developer, etc.)
  - Last updated date
- Click to open project settings

#### 6.2 Create Project Card Component
**Files to create:**
- `src/Automaton.Studio/Pages/Projects/Components/ProjectCard.razor`
- `src/Automaton.Studio/Pages/Projects/Components/ProjectCard.razor.cs`

**Display:**
- Project icon/logo
- Name and description
- Stats (members, apps, flows)
- User's role badge
- Actions (settings, delete)

#### 6.3 Create Project Dialog
**Files to create:**
- `src/Automaton.Studio/Pages/Projects/Components/CreateProjectDialog.razor`

**Form fields:**
- Name (required, unique)
- Display Name (required)
- Description (optional)

#### 6.4 Add Route
**File to modify:**
- Ensure route `/projects` is mapped to ProjectsPage

### Validation
- [ ] Projects page loads successfully
- [ ] Projects are displayed correctly
- [ ] Create project dialog works
- [ ] New project appears in list
- [ ] Delete project works (with confirmation)
- [ ] UI is responsive and matches design

### Review Checkpoint
Commit and create PR with screenshots. Wait for approval before proceeding.

---

## Stage 7: Project Settings Page (Week 4)

### Goal
Create detailed project settings page with multiple tabs.

### Tasks

#### 7.1 Create Project Settings Page
**Files to create:**
- `src/Automaton.Studio/Pages/Projects/ProjectSettingsPage.razor`
- `src/Automaton.Studio/Pages/Projects/ProjectSettingsPage.razor.cs`

**Tabs:**
1. General - Name, description, logo
2. Members - Member list and management
3. Resources - Apps, flows, runners linked to project
4. Settings - Advanced settings

#### 7.2 Create Members Panel
**Files to create:**
- `src/Automaton.Studio/Pages/Projects/Components/MembersPanel.razor`
- `src/Automaton.Studio/Pages/Projects/Components/AddMemberDialog.razor`

**Features:**
- Table/list of members
- Columns: Name, Email, Role, Joined Date, Actions
- Add member button
- Role dropdown (for admins only)
- Remove member button (with last admin protection)

#### 7.3 Create Add Member Dialog
**Form fields:**
- Email (required)
- Role (dropdown: ProjectAdmin, Developer, AppUser, Viewer)
- Send invitation checkbox (default: true)

**Logic:**
- Check if user exists
- If not, create account and send invitation
- If yes, add to project

#### 7.4 Create Resources Panel
**Files to create:**
- `src/Automaton.Studio/Pages/Projects/Components/ResourcesPanel.razor`

**Features:**
- Tabs for Apps, Flows, Runners, Steps
- List of linked resources
- "Add Resource" button
- Resource selector dialog
- Unlink button

### Validation
- [ ] Settings page loads correctly
- [ ] All tabs switch properly
- [ ] Members list displays correctly
- [ ] Add member works (with email invitation)
- [ ] Role changes work
- [ ] Remove member works (prevents last admin)
- [ ] Resources can be linked/unlinked

### Review Checkpoint
Commit and create PR with screenshots. Wait for approval before proceeding.

---

## Stage 8: Navigation Integration (Week 4)

### Goal
Integrate Projects into main navigation and add enterprise feature flag.

### Tasks

#### 8.1 Add Feature Flag
**File to modify:**
- Update ApplicationUser entity (already done in Stage 1)
- Add API endpoint to check/toggle enterprise mode

#### 8.2 Update Main Menu
**File to modify:**
- `src/Automaton.Studio/Shared/MainMenu.razor`

**Changes:**
- Add "Projects" menu item (conditionally shown if IsEnterpriseEnabled)
- Add project switcher dropdown (shows after user selects a project)

#### 8.3 Create Project Switcher Component
**Files to create:**
- `src/Automaton.Studio/Shared/Components/ProjectSwitcher.razor`

**Features:**
- Dropdown showing:
  - Personal Workspace
  - List of user's projects (with role badge)
  - "Create New Project..." option
- Clicking switches active project context
- Updates ProjectContextService

#### 8.4 Add Enterprise Settings Page
**Files to create:**
- `src/Automaton.Studio/Pages/Settings/EnterpriseSettingsPage.razor`

**Features:**
- Toggle "Enable Enterprise Features"
- Explanation of what enterprise features include
- Only visible to SysAdmin initially

### Validation
- [ ] Projects menu item appears when feature is enabled
- [ ] Projects menu hidden when feature is disabled
- [ ] Project switcher works correctly
- [ ] Current project persists across navigation
- [ ] Enterprise toggle works

### Review Checkpoint
Commit and create PR with screenshots. Wait for approval before proceeding.

---

## Stage 9: Resource Linking (Week 5)

### Goal
Enable adding existing apps/flows to projects with proper access control.

### Tasks

#### 9.1 Modify Apps Page
**File to modify:**
- `src/Automaton.Studio/Pages/Apps/AppsPage.razor`

**Changes:**
- Add project filter dropdown
- Show "Add to Project" button for personal apps
- Show "Remove from Project" button for project apps
- Filter apps by selected project

#### 9.2 Create Add to Project Dialog
**Files to create:**
- `src/Automaton.Studio/Pages/Apps/Components/AddToProjectDialog.razor`

**Features:**
- Multi-select dropdown of user's projects
- Option to change scope from Personal to Project
- Confirmation message

#### 9.3 Update Apps Service
**File to modify:**
- `src/Automaton.Studio/Services/AppsService.cs`

**Add methods:**
- `LinkAppToProjectAsync(appId, projectId)`
- `UnlinkAppFromProjectAsync(appId, projectId)`
- `UpdateAppScopeAsync(appId, newScope)`

#### 9.4 Update WebApp Access Control
**Files to modify:**
- `src/Automaton.Studio.Server/Controllers/AppsController.cs`
- Update authorization to check project membership for project-scoped apps

#### 9.5 Repeat for Flows, Runners, Steps
Apply similar changes to:
- Flows page
- Runners page
- Custom Steps page

### Validation
- [ ] Personal apps can be added to projects
- [ ] Apps filter by project correctly
- [ ] Access control enforced (non-members can't see project apps)
- [ ] Remove from project works
- [ ] Same functionality works for flows, runners, steps

### Review Checkpoint
Commit and create PR with screenshots. Wait for approval before proceeding.

---

## Stage 10: Dashboard Personalization (Week 6)

### Goal
Create role-based dashboard views that show appropriate content for each user role.

### Tasks

#### 10.1 Detect User Role in Current Project
**File to modify:**
- `src/Automaton.Studio/Pages/DashboardPage.razor.cs`

**Logic:**
```csharp
var currentProject = await ProjectContextService.GetCurrentProjectAsync();
if (currentProject != null)
{
    var userRole = await ProjectContextService.GetUserRoleInProjectAsync(currentProject.Id);
    // Render dashboard based on role
}
```

#### 10.2 Create AppUser Dashboard View
**Files to create:**
- `src/Automaton.Studio/Pages/Dashboard/AppUserDashboard.razor`

**Features:**
- Large card-based layout
- Only shows apps user can access
- No developer tools visible
- App cards with:
  - Icon/logo
  - Name and description
  - "Open App" button
- Search and filter

#### 10.3 Create Developer Dashboard View
**Files to create:**
- `src/Automaton.Studio/Pages/Dashboard/DeveloperDashboard.razor`

**Features:**
- Quick actions section (Create App, Create Flow)
- Project statistics
- Recent activity in project
- Links to development tools

#### 10.4 Update Navigation Visibility
**File to modify:**
- `src/Automaton.Studio/Shared/MainMenu.razor`

**Logic:**
```csharp
var showDevTools = userRole == ProjectRole.ProjectAdmin || 
                   userRole == ProjectRole.Developer;

if (!showDevTools)
{
    // Hide: Flows, Steps, Runners, API Keys, etc.
    // Show: Only Apps, Project Switcher, Account
}
```

#### 10.5 Create Viewer Dashboard View
**Files to create:**
- `src/Automaton.Studio/Pages/Dashboard/ViewerDashboard.razor`

**Features:**
- Read-only view of project
- Statistics and analytics
- No action buttons
- Links to logs and history

### Validation
- [ ] AppUser sees only app cards, no dev tools
- [ ] Developer sees full tools + project apps
- [ ] Viewer sees read-only dashboard
- [ ] Navigation menu updates based on role
- [ ] Dashboard switches when changing projects
- [ ] Statistics display correctly

### Review Checkpoint
Commit and create PR with screenshots. Wait for approval before proceeding.

---

## Stage 11: Invitation Flow (Week 6-7)

### Goal
Implement email invitation system for adding new users to projects.

### Tasks

#### 11.1 Create Invitation Email Template
**Files to create:**
- `src/Automaton.Studio.Server/Templates/ProjectInvitationEmail.html`

**Content:**
- Project name and description
- Inviter's name
- Role being assigned
- Accept invitation link
- Expiration time (24 hours)

#### 11.2 Create Invitation Token System
**File to create:**
- `src/Automaton.Studio.Server/Services/InvitationTokenService.cs`

**Methods:**
- `GenerateInvitationToken(projectId, email, role, invitedBy)`
- `ValidateInvitationToken(token)` - returns invitation details or null if expired
- `ConsumeInvitationToken(token)` - marks as used

#### 11.3 Update AddMemberAsync
**File to modify:**
- `src/Automaton.Studio.Server/Application/Services/ProjectMemberService.cs`

**Logic:**
```csharp
public async Task<InvitationResult> AddMemberAsync(projectId, email, role, invitedBy)
{
    // Check if user exists
    var user = await _userManager.FindByEmailAsync(email);
    
    if (user == null)
    {
        // Create user account (unverified)
        user = await CreateUserAccountAsync(email);
    }
    
    // Generate invitation token
    var token = _invitationService.GenerateToken(projectId, email, role, invitedBy);
    
    // Send email
    await _emailService.SendInvitationEmail(email, token, project, inviter);
    
    return new InvitationResult { UserId = user.Id, TokenSent = true };
}
```

#### 11.4 Create Accept Invitation Page
**Files to create:**
- `src/Automaton.Studio/Pages/Projects/AcceptInvitationPage.razor`
- Route: `/projects/invite/{token}`

**Flow:**
1. Validate token
2. If user not logged in → redirect to login/register
3. If user logged in → show confirmation dialog
4. Accept → add user to project with specified role
5. Redirect to project dashboard

#### 11.5 Send Email via Email Service
**File to modify:**
- Use existing email service or integrate with SendGrid/SMTP

### Validation
- [ ] Invitation email sent successfully
- [ ] Token validation works
- [ ] Expired tokens rejected
- [ ] Accept invitation adds user to project
- [ ] User receives welcome notification
- [ ] Non-existent users can register and accept

### Review Checkpoint
Commit and create PR for review. Test invitation flow end-to-end. Wait for approval before proceeding.

---

## Stage 12: Access Control Enforcement (Week 7)

### Goal
Ensure all resource access is properly gated by project membership and role.

### Tasks

#### 12.1 Update WebApp Access Check
**Files to modify:**
- `src/Automaton.Studio/Pages/Apps/WebAppViewer.razor.cs`
- `src/Automaton.Studio.Server/Controllers/PublicWebAppsController.cs`

**Logic:**
```csharp
if (app.Scope == AccessScope.Project && app.ProjectId.HasValue)
{
    var canAccess = await _authService.CanAccessApp(userId, app.Id);
    if (!canAccess)
    {
        return Forbid(); // 403
    }
}
```

#### 12.2 Update Flow Execution Check
**Files to modify:**
- Flow execution controllers
- Ensure project members with appropriate roles can execute

#### 12.3 Update Runner Access Check
**Files to modify:**
- Runner registration and management
- Ensure only project members can see/use project runners

#### 12.4 Update Custom Step Access
**Files to modify:**
- Custom steps management
- Enforce project-level access

#### 12.5 Add Audit Logging
**File to create:**
- `src/Automaton.Studio.Server/Services/ProjectAuditService.cs`

**Log events:**
- Project created/deleted
- Member added/removed/role changed
- Resource linked/unlinked
- Access denied events (security)

### Validation
- [ ] Non-members cannot access project resources
- [ ] AppUsers cannot access dev tools
- [ ] Developers can access apps and flows
- [ ] Audit logs capture key events
- [ ] Authorization failures return 403 Forbidden

### Review Checkpoint
Commit and create PR for review. Perform security testing. Wait for approval before proceeding.

---

## Stage 13: UI Polish & UX Improvements (Week 8)

### Goal
Polish the UI, add animations, improve error messages, and enhance overall UX.

### Tasks

#### 13.1 Add Loading States
- Skeleton screens while loading projects
- Spinners for async operations
- Progress indicators for long-running tasks

#### 13.2 Add Empty States
- "No projects yet" screen with call-to-action
- "No members" state in members panel
- "No apps" state for AppUsers

#### 13.3 Add Confirmation Dialogs
- Confirm before deleting project
- Confirm before removing last admin
- Confirm before unlinking resources

#### 13.4 Improve Error Messages
- User-friendly error messages
- Inline validation for forms
- Toast notifications for success/error

#### 13.5 Add Onboarding Flow
**Files to create:**
- `src/Automaton.Studio/Pages/Projects/ProjectOnboarding.razor`

**Steps:**
1. Welcome to Projects
2. Create your first project
3. Add a member
4. Link an app to the project
5. View as AppUser

#### 13.6 Add Help/Documentation Links
- Tooltips explaining roles
- Help icons linking to documentation
- Inline guidance for first-time users

#### 13.7 Responsive Design
- Ensure all pages work on mobile/tablet
- Responsive grid layouts
- Mobile-friendly navigation

### Validation
- [ ] Loading states smooth and consistent
- [ ] Empty states helpful and actionable
- [ ] Error messages clear and specific
- [ ] Onboarding flow works end-to-end
- [ ] UI responsive on all screen sizes
- [ ] Animations smooth and not distracting

### Review Checkpoint
Commit and create PR with screenshots/video. Wait for approval before proceeding.

---

## Stage 14: Testing & Bug Fixes (Week 8)

### Goal
Comprehensive testing and bug fixes before final release.

### Tasks

#### 14.1 Unit Test Coverage
- Ensure 80%+ coverage for:
  - Services
  - Controllers
  - Authorization handlers
- Fix any failing tests

#### 14.2 Integration Testing
- Test all API endpoints
- Test authorization for each role
- Test edge cases (last admin, invalid data, etc.)

#### 14.3 End-to-End Testing
- Create project → Add members → Link apps → View as AppUser
- Test invitation flow end-to-end
- Test project switching
- Test dashboard personalization

#### 14.4 Security Testing
- Attempt to access other projects
- Attempt privilege escalation
- Test with invalid/expired tokens
- SQL injection testing
- XSS testing

#### 14.5 Performance Testing
- Load test with 100 projects
- Test with 50+ members in project
- Query optimization
- Index tuning

#### 14.6 Browser Testing
- Test on Chrome, Firefox, Safari, Edge
- Test on mobile browsers
- Fix browser-specific issues

#### 14.7 Accessibility Testing
- Keyboard navigation
- Screen reader compatibility
- ARIA labels
- Color contrast

### Validation
- [ ] All unit tests pass
- [ ] All integration tests pass
- [ ] E2E tests pass
- [ ] No security vulnerabilities
- [ ] Performance acceptable (<500ms API response)
- [ ] Works on all major browsers
- [ ] Accessible (WCAG 2.1 Level AA)

### Review Checkpoint
Comprehensive review and testing. Document any known issues or limitations.

---

## Stage 15: Documentation & Deployment (Week 8)

### Goal
Finalize documentation and prepare for production deployment.

### Tasks

#### 15.1 Update API Documentation
- Ensure Swagger docs are complete
- Add request/response examples
- Document authorization requirements

#### 15.2 Create User Guide
**Files to create:**
- `docs/projects/user-guide.md`

**Sections:**
- What are Projects?
- How to create a project
- How to invite members
- Understanding roles
- How to link apps to projects
- FAQ

#### 15.3 Create Admin Guide
**Files to create:**
- `docs/projects/admin-guide.md`

**Sections:**
- Project management best practices
- Managing members and roles
- Security considerations
- Troubleshooting

#### 15.4 Update CHANGELOG
- Document all changes
- Breaking changes (none expected)
- New features
- Bug fixes

#### 15.5 Deployment Checklist
- [ ] Run database migration on staging
- [ ] Test on staging environment
- [ ] Backup production database
- [ ] Run migration on production
- [ ] Deploy API server
- [ ] Deploy web app
- [ ] Smoke tests on production
- [ ] Monitor for errors

#### 15.6 Feature Announcement
- Blog post announcing feature
- Email to enterprise customers
- Update marketing materials

### Validation
- [ ] All documentation complete and accurate
- [ ] Deployment successful
- [ ] Production environment stable
- [ ] Monitoring alerts configured
- [ ] Feature announced

### Review Checkpoint
Final sign-off and production deployment.

---

## Stage 16: Post-Launch Monitoring (Ongoing)

### Goal
Monitor feature adoption, gather feedback, and iterate.

### Tasks

#### 16.1 Monitor Metrics
- Track project creation rate
- Track member invitation rate
- Track dashboard views by role
- Monitor API response times
- Track error rates

#### 16.2 Gather Feedback
- Survey enterprise customers
- Monitor support tickets
- Collect feature requests
- Track NPS score

#### 16.3 Iterate and Improve
- Prioritize bug fixes
- Plan Phase 2 enhancements:
  - Custom roles
  - Project templates
  - Advanced analytics
  - Cross-project resource sharing

---

## Summary

This implementation plan breaks down the Project/Organization feature into 16 manageable stages spanning approximately 8 weeks. Each stage has:

- **Clear goals** - What to accomplish
- **Specific tasks** - What files to create/modify
- **Validation criteria** - How to verify it works
- **Review checkpoints** - When to pause for review

**Key Principles:**
- ✅ Incremental progress (each stage builds on previous)
- ✅ Testable milestones (validate before moving forward)
- ✅ Reviewable commits (checkpoint after each stage)
- ✅ No breaking changes (backward compatible throughout)
- ✅ App-centric focus (no FlowUser role, emphasis on app execution)

**Recommended Approach:**
1. Implement one stage at a time
2. Commit and create PR after each stage
3. Wait for review/approval before next stage
4. Run all tests before each commit
5. Update documentation as you go

**Success Criteria:**
- All stages completed
- All tests passing
- All review checkpoints approved
- Feature deployed to production
- No regression in existing functionality

---

**Document Version:** 1.0  
**Created:** November 11, 2024  
**Status:** Ready for Implementation
