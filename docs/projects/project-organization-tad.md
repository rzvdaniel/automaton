# Project & Organization Multi-Tenancy - Technical Approach Document

## Table of Contents
1. [Executive Summary](#executive-summary)
2. [Business Context & Goals](#business-context--goals)
3. [Current State Analysis](#current-state-analysis)
4. [Proposed Solution Architecture](#proposed-solution-architecture)
5. [Database Schema Design](#database-schema-design)
6. [Access Control & Permission Model](#access-control--permission-model)
7. [API Design](#api-design)
8. [UI/UX Changes](#uiux-changes)
9. [Migration Strategy](#migration-strategy)
10. [Implementation Phases](#implementation-phases)
11. [Security Considerations](#security-considerations)
12. [Testing Strategy](#testing-strategy)
13. [Open Questions & Discussion Points](#open-questions--discussion-points)

---

## Executive Summary

This document outlines the technical approach for implementing **Project/Organization** multi-tenancy capabilities in Automaton Studio. This feature enables enterprise customers to create isolated workspaces (Projects) where they can:

- Add team members with specific roles (Admin, Developer, App User, Flow User)
- Share Apps, Flows, Runners, and Custom Steps within their organization
- Control access through Role-Based Access Control (RBAC)
- Maintain clear separation between development tools and end-user experiences

**Key Benefits:**
- ✅ Industry-standard multi-tenancy for B2B SaaS readiness
- ✅ Enterprise subscription differentiation from standard users
- ✅ Enhanced security through project-level isolation
- ✅ Flexible user roles supporting both developers and end-users
- ✅ Backward compatibility with existing single-user model

**Timeline Estimate:** 6-8 weeks for Phase 1 (Core Infrastructure + Basic Project Management)

---

## Business Context & Goals

### Current Model: Individual Creator/User
Currently, Automaton operates as a **single-tenant per user** platform:
- Each user has full access to creation tools (Apps, Flows, Steps, Runners, API Keys)
- Users can publish Apps/Flows publicly or keep them private
- No concept of team/organization collaboration
- Perfect for individual developers and hobbyists

### Target Model: Enterprise Multi-Tenancy
The enterprise model introduces **Projects** as first-class entities:
- **Standard Subscription**: Keeps current model (backward compatible)
- **Enterprise Subscription**: Unlocks Project capabilities
  - Create multiple Projects (teams/departments)
  - Add users to Projects with specific roles
  - Share resources (Apps, Flows) within Project
  - Separate "creator" tools from "end-user" portals

### Use Cases

#### Use Case 1: Enterprise Development Team
**Scenario:** A software company uses Automaton for internal tools.
- **Project Owner** (Alice) creates "Engineering Tools" project
- Adds **Developers** (Bob, Carol) with full creation access
- Adds **App Users** (50 employees) who only see/use published Apps
- Apps are restricted to project members, not public

#### Use Case 2: Multi-Department Organization
**Scenario:** A large company has multiple teams using Automaton.
- **System Admin** creates projects: "Marketing", "Sales", "IT"
- Each project has its own **Admin**, **Developers**, and **App Users**
- Resources don't leak between projects
- Users can be members of multiple projects

#### Use Case 3: Contractor/Consulting Model
**Scenario:** A consulting firm manages automation for multiple clients.
- Create one Project per client
- Consultants are **Developers** across multiple projects
- Client employees are **App Users** in their respective project
- Clear separation and billing per client

---

## Current State Analysis

### Existing Architecture

#### Database Entities
```
ApplicationUser (Identity)
  ├── UserRoles (many-to-many with Roles)
  └── Ownership links:
       ├── FlowUsers (can access Flow)
       ├── WebAppUsers (can access WebApp)
       ├── RunnerUsers (can access Runner)
       ├── CustomStepUsers (can access Step)
       └── ScheduleUsers (can access Schedule)
```

#### Current User Roles
Defined in `UserRole.cs`:
- `SysAdmin`: System administrator
- `Admin`: Administrator
- `User`: Standard user

**Gap:** No distinction between "creator/developer" vs "app consumer/end-user"

#### Current Access Model
- **User-centric ownership**: Each entity (Flow, App, Runner) is linked to specific users via junction tables
- **No grouping mechanism**: No way to define "all users in Marketing team can access these 10 apps"
- **Binary access**: Either you have access or you don't (no read-only vs execute vs admin)

### What Works Well
✅ Foundation for multi-tenancy exists (user junction tables)
✅ Identity system (ASP.NET Core Identity) supports claims/roles
✅ Modular architecture (clear separation of concerns)
✅ API-first design makes RBAC implementation cleaner

### What Needs to Change
❌ No Project/Organization entity
❌ No role-based permissions (Developer, AppUser, FlowUser)
❌ No project-level resource isolation
❌ Dashboard shows all tools, not personalized by role
❌ WebApp "Protected" mode applies to all authenticated users, not project members

---

## Proposed Solution Architecture

### High-Level Architecture

```
┌────────────────────────────────────────────────────────────────┐
│                     Automaton Platform                          │
├────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────┐          ┌──────────────────┐            │
│  │  Standard User  │          │ Enterprise User   │            │
│  │  (Solo Creator) │          │  (Project Owner)  │            │
│  └────────┬────────┘          └─────────┬─────────┘            │
│           │                              │                      │
│           │ Creates/Uses                 │ Creates              │
│           ▼                              ▼                      │
│  ┌─────────────────┐          ┌──────────────────┐            │
│  │ Personal Workspace│         │   PROJECT(S)     │            │
│  │ - Apps           │         │  ┌──────────────┐│            │
│  │ - Flows          │         │  │ Project A    ││            │
│  │ - Steps          │         │  │ ├─ Members   ││            │
│  │ - Runners        │         │  │ ├─ Apps      ││            │
│  └─────────────────┘          │  │ ├─ Flows     ││            │
│                                │  │ └─ Runners   ││            │
│                                │  └──────────────┘│            │
│                                │  ┌──────────────┐│            │
│                                │  │ Project B    ││            │
│                                │  │ ├─ Members   ││            │
│                                │  │ ├─ Apps      ││            │
│                                │  │ └─ ...       ││            │
│                                │  └──────────────┘│            │
│                                └──────────────────┘            │
└────────────────────────────────────────────────────────────────┘
```

### Dual-Mode Operation

**Mode 1: Standard (Current Behavior)**
- User creates account → full access to all tools
- Resources are personal (user-owned)
- Can publish Apps publicly or keep private
- No Projects

**Mode 2: Enterprise (New Behavior)**
- **Project Owner** creates Projects
- Invites users with roles: Admin, Developer, AppUser, FlowUser
- Resources belong to Projects, not individual users
- Users see personalized dashboards based on role:
  - **Developer Role**: Full creation tools + project apps
  - **AppUser Role**: Only Apps (card-based portal)
  - **FlowUser Role**: Only Flows (execute-only view)
- Apps can be "Project-Protected" (only project members)

### Backward Compatibility Strategy

**Option A: Automatic Migration (Recommended)**
- Existing users remain in "Standard Mode" by default
- Resources stay user-owned (no breaking changes)
- Opt-in to Projects: UI prompt to "Upgrade to Projects" or "Create Organization"
- Once upgraded, user can choose to:
  - Keep personal workspace AND create projects
  - Migrate personal resources into a project

**Option B: Transparent Wrapper**
- Behind the scenes, create a "Personal Project" for each existing user
- Migrate their resources into this project
- They're the only member/owner
- Allows unified code path (everything is project-scoped)
- Risk: More complex migration, potential data issues

**Recommendation:** Start with **Option A** for Phase 1. Simpler, safer, and allows gradual adoption.

---

## Database Schema Design

### New Entities

#### 1. Project Entity
```csharp
// Location: src/Automaton.Studio.Server/Entities/Project.cs
public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; }                    // Unique name/slug
    public string DisplayName { get; set; }             // Friendly name
    public string? Description { get; set; }
    
    // Owner who created the project
    public Guid OwnerId { get; set; }
    public ApplicationUser Owner { get; set; }
    
    // Settings
    public string? LogoUrl { get; set; }
    public string? Theme { get; set; }                  // JSON: colors, branding
    
    // Subscription/Billing (future)
    public string? SubscriptionTier { get; set; }       // "Enterprise", "Team", etc.
    public DateTime? SubscriptionExpiresAt { get; set; }
    
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Navigation properties
    public ICollection<ProjectMember> Members { get; set; }
    public ICollection<ProjectApp> ProjectApps { get; set; }
    public ICollection<ProjectFlow> ProjectFlows { get; set; }
    public ICollection<ProjectRunner> ProjectRunners { get; set; }
    public ICollection<ProjectCustomStep> ProjectCustomSteps { get; set; }
}
```

#### 2. ProjectMember Entity
```csharp
// Location: src/Automaton.Studio.Server/Entities/ProjectMember.cs
public class ProjectMember
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; }
    
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; }
    
    // Role within this project
    public string Role { get; set; }                    // ProjectRole enum values
    
    // Invitation tracking
    public DateTime JoinedAt { get; set; }
    public Guid? InvitedBy { get; set; }
    public ApplicationUser? Inviter { get; set; }
    
    public bool IsActive { get; set; } = true;
}
```

#### 3. ProjectRole (Static Class/Enum)
```csharp
// Location: src/Automaton.Studio.Server/Enums/ProjectRole.cs
public static class ProjectRole
{
    // Can manage project, add members, assign roles
    public const string ProjectAdmin = "ProjectAdmin";
    
    // Can create/edit flows, apps, steps - full development access
    public const string Developer = "Developer";
    
    // Can only view and execute Apps
    public const string AppUser = "AppUser";
    
    // Can only view and execute Flows (via API/schedule)
    public const string FlowUser = "FlowUser";
    
    // Read-only access (view only, no execute)
    public const string Viewer = "Viewer";
    
    // Helper groups
    public const string Admins = $"{ProjectAdmin}";
    public const string Creators = $"{ProjectAdmin},{Developer}";
    public const string AllMembers = $"{ProjectAdmin},{Developer},{AppUser},{FlowUser},{Viewer}";
}
```

#### 4. Resource-to-Project Linking Tables

```csharp
// Location: src/Automaton.Studio.Server/Entities/ProjectApp.cs
public class ProjectApp
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; }
    
    public Guid WebAppId { get; set; }
    public WebApp WebApp { get; set; }
    
    public DateTime AddedAt { get; set; }
    public Guid AddedBy { get; set; }
}

// Similar for ProjectFlow, ProjectRunner, ProjectCustomStep
public class ProjectFlow
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; }
    
    public Guid FlowId { get; set; }
    public Flow Flow { get; set; }
    
    public DateTime AddedAt { get; set; }
    public Guid AddedBy { get; set; }
}

public class ProjectRunner
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; }
    
    public Guid RunnerId { get; set; }
    public Runner Runner { get; set; }
    
    public DateTime AddedAt { get; set; }
    public Guid AddedBy { get; set; }
}

public class ProjectCustomStep
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; }
    
    public Guid CustomStepId { get; set; }
    public CustomStep CustomStep { get; set; }
    
    public DateTime AddedAt { get; set; }
    public Guid AddedBy { get; set; }
}
```

### Modified Entities

#### ApplicationUser - Add Project Context
```csharp
public class ApplicationUser : IdentityUser<Guid>
{
    public string Name { get; set; }
    public ICollection<ApplicationUserRole> UserRoles { get; set; }
    
    // NEW: Projects owned by this user
    public ICollection<Project> OwnedProjects { get; set; }
    
    // NEW: Project memberships
    public ICollection<ProjectMember> ProjectMemberships { get; set; }
    
    // NEW: Current/Default project context (optional UX enhancement)
    public Guid? CurrentProjectId { get; set; }
    
    // KEEP: Existing personal resource links (backward compatibility)
    public ICollection<FlowUser> FlowUsers { get; set; }
    public ICollection<WebAppUser> WebAppUsers { get; set; }
    // ... etc
}
```

#### WebApp - Add Project Access Control
```csharp
public class WebApp
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public string Definition { get; set; }
    
    // Publishing fields
    public bool IsPublished { get; set; }
    public string? PublicUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public int Version { get; set; } = 1;
    public Guid? ExecutionRunnerId { get; set; }
    
    // Access control
    public bool RequiresAuthentication { get; set; } = false;
    public string? AllowedRoles { get; set; }
    
    // NEW: Project-level access control
    public AccessScope Scope { get; set; } = AccessScope.Personal;
    public Guid? ProjectId { get; set; }                // If Scope = Project
    
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    
    // Existing personal access (backward compat)
    public ICollection<WebAppUser> WebAppUsers { get; set; }
    
    // NEW: Project links
    public ICollection<ProjectApp> ProjectApps { get; set; }
}

// NEW enum
public enum AccessScope
{
    Personal = 0,      // User-owned (current behavior)
    Project = 1,       // Project-owned (enterprise)
    Public = 2         // Public access (published)
}
```

### Database Diagram

```
┌─────────────────┐
│ ApplicationUser │
├─────────────────┤
│ Id (PK)         │
│ Name            │
│ Email           │
│ CurrentProjectId│◄──┐
└────────┬────────┘   │
         │            │
         │ Owns       │
         ▼            │
┌─────────────────┐   │
│    Project      │   │
├─────────────────┤   │
│ Id (PK)         │───┘
│ Name            │
│ DisplayName     │
│ OwnerId (FK)    │
└────────┬────────┘
         │
         │ Has Many
         ▼
┌─────────────────┐         ┌──────────────┐
│ ProjectMember   │         │  ProjectApp  │
├─────────────────┤         ├──────────────┤
│ ProjectId (FK)  │         │ProjectId (FK)│
│ UserId (FK)     │         │WebAppId (FK) │
│ Role            │         └──────┬───────┘
│ JoinedAt        │                │
└─────────────────┘                │
                                   ▼
┌─────────────────┐         ┌──────────────┐
│    WebApp       │◄────────┤ WebAppUser   │
├─────────────────┤         │ (Personal)   │
│ Id (PK)         │         └──────────────┘
│ Name            │
│ Scope           │ ◄── Personal/Project/Public
│ ProjectId (FK)? │
└─────────────────┘

(Similar structure for Flow, Runner, CustomStep)
```

### Migration Script Outline

```sql
-- Phase 1: Add new tables
CREATE TABLE Projects (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL UNIQUE,
    DisplayName NVARCHAR(200),
    Description NVARCHAR(MAX),
    OwnerId UNIQUEIDENTIFIER NOT NULL,
    Created DATETIME2 NOT NULL,
    Updated DATETIME2 NOT NULL,
    IsActive BIT DEFAULT 1,
    FOREIGN KEY (OwnerId) REFERENCES AspNetUsers(Id)
);

CREATE TABLE ProjectMembers (
    ProjectId UNIQUEIDENTIFIER NOT NULL,
    UserId UNIQUEIDENTIFIER NOT NULL,
    Role NVARCHAR(50) NOT NULL,
    JoinedAt DATETIME2 NOT NULL,
    InvitedBy UNIQUEIDENTIFIER,
    IsActive BIT DEFAULT 1,
    PRIMARY KEY (ProjectId, UserId),
    FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
    FOREIGN KEY (UserId) REFERENCES AspNetUsers(Id) ON DELETE NO ACTION
);

CREATE TABLE ProjectApps (
    ProjectId UNIQUEIDENTIFIER NOT NULL,
    WebAppId UNIQUEIDENTIFIER NOT NULL,
    AddedAt DATETIME2 NOT NULL,
    AddedBy UNIQUEIDENTIFIER NOT NULL,
    PRIMARY KEY (ProjectId, WebAppId),
    FOREIGN KEY (ProjectId) REFERENCES Projects(Id) ON DELETE CASCADE,
    FOREIGN KEY (WebAppId) REFERENCES WebApps(Id) ON DELETE CASCADE
);

-- Similar for ProjectFlows, ProjectRunners, ProjectCustomSteps

-- Phase 2: Extend existing tables
ALTER TABLE WebApps ADD Scope INT DEFAULT 0;  -- 0=Personal
ALTER TABLE WebApps ADD ProjectId UNIQUEIDENTIFIER NULL;
ALTER TABLE ApplicationUser ADD CurrentProjectId UNIQUEIDENTIFIER NULL;

-- Phase 3: Indexes for performance
CREATE INDEX IX_ProjectMembers_UserId ON ProjectMembers(UserId);
CREATE INDEX IX_ProjectMembers_Role ON ProjectMembers(Role);
CREATE INDEX IX_ProjectApps_WebAppId ON ProjectApps(WebAppId);
CREATE INDEX IX_WebApps_Scope ON WebApps(Scope);
CREATE INDEX IX_WebApps_ProjectId ON WebApps(ProjectId) WHERE ProjectId IS NOT NULL;
```

---

## Access Control & Permission Model

### Permission Matrix

| Resource Type | Personal Owner | Project Admin | Developer | AppUser | FlowUser | Viewer |
|---------------|----------------|---------------|-----------|---------|----------|--------|
| **Project**   |                |               |           |         |          |        |
| - View        | ✅             | ✅            | ✅        | ✅      | ✅       | ✅     |
| - Edit Settings| ✅            | ✅            | ❌        | ❌      | ❌       | ❌     |
| - Add Members | ✅             | ✅            | ❌        | ❌      | ❌       | ❌     |
| - Delete      | ✅             | ❌            | ❌        | ❌      | ❌       | ❌     |
| **Apps**      |                |               |           |         |          |        |
| - View List   | ✅             | ✅            | ✅        | ✅      | ❌       | ✅     |
| - Create      | ✅             | ✅            | ✅        | ❌      | ❌       | ❌     |
| - Edit        | ✅ (own)       | ✅            | ✅        | ❌      | ❌       | ❌     |
| - Delete      | ✅ (own)       | ✅            | ✅ (own)  | ❌      | ❌       | ❌     |
| - Execute     | ✅             | ✅            | ✅        | ✅      | ❌       | ❌     |
| **Flows**     |                |               |           |         |          |        |
| - View List   | ✅             | ✅            | ✅        | ❌      | ✅       | ✅     |
| - Create      | ✅             | ✅            | ✅        | ❌      | ❌       | ❌     |
| - Edit        | ✅ (own)       | ✅            | ✅        | ❌      | ❌       | ❌     |
| - Delete      | ✅ (own)       | ✅            | ✅ (own)  | ❌      | ❌       | ❌     |
| - Execute     | ✅             | ✅            | ✅        | ❌      | ✅       | ❌     |
| **Runners**   |                |               |           |         |          |        |
| - View        | ✅             | ✅            | ✅        | ❌      | ❌       | ✅     |
| - Register    | ✅             | ✅            | ✅        | ❌      | ❌       | ❌     |
| - Configure   | ✅ (own)       | ✅            | ✅        | ❌      | ❌       | ❌     |
| **Custom Steps**|              |               |           |         |          |        |
| - View        | ✅             | ✅            | ✅        | ❌      | ❌       | ✅     |
| - Create      | ✅             | ✅            | ✅        | ❌      | ❌       | ❌     |
| - Edit        | ✅ (own)       | ✅            | ✅        | ❌      | ❌       | ❌     |

### Authorization Implementation Strategy

#### Option 1: Policy-Based Authorization (Recommended)
Use ASP.NET Core's built-in policy system with custom requirements.

```csharp
// Location: src/Automaton.Studio.Server/Authorization/ProjectPolicyRequirements.cs
public class ProjectMemberRequirement : IAuthorizationRequirement
{
    public string[] AllowedRoles { get; }
    
    public ProjectMemberRequirement(params string[] allowedRoles)
    {
        AllowedRoles = allowedRoles;
    }
}

public class ProjectMemberHandler : AuthorizationHandler<ProjectMemberRequirement>
{
    private readonly IDataContext _context;
    
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ProjectMemberRequirement requirement)
    {
        var userId = context.User.GetUserId();
        var projectId = context.Resource as Guid?;
        
        if (projectId == null) 
        {
            context.Fail();
            return;
        }
        
        var member = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => 
                pm.ProjectId == projectId && 
                pm.UserId == userId && 
                pm.IsActive);
        
        if (member != null && requirement.AllowedRoles.Contains(member.Role))
        {
            context.Succeed(requirement);
        }
    }
}

// Registration in Program.cs
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

#### Usage in Controllers
```csharp
[Authorize(Policy = "ProjectDeveloper")]
[HttpPost("api/projects/{projectId}/apps")]
public async Task<IActionResult> CreateApp(Guid projectId, [FromBody] NewApp app)
{
    // User is guaranteed to be Project Admin or Developer
    // ...
}
```

#### Option 2: Service-Layer Authorization
For more complex scenarios, implement authorization in service classes.

```csharp
// Location: src/Automaton.Studio.Server/Application/Services/ProjectAuthorizationService.cs
public class ProjectAuthorizationService
{
    private readonly IDataContext _context;
    
    public async Task<bool> CanAccessProject(Guid userId, Guid projectId)
    {
        return await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && 
                           pm.UserId == userId && 
                           pm.IsActive);
    }
    
    public async Task<bool> HasRole(Guid userId, Guid projectId, params string[] roles)
    {
        var member = await _context.ProjectMembers
            .FirstOrDefaultAsync(pm => 
                pm.ProjectId == projectId && 
                pm.UserId == userId && 
                pm.IsActive);
        
        return member != null && roles.Contains(member.Role);
    }
    
    public async Task<bool> CanModifyResource<T>(Guid userId, Guid resourceId) 
        where T : class
    {
        // Check if user is owner OR project admin/developer
        // Implementation depends on resource type
    }
}
```

### Resource Access Logic

#### Determining What a User Can See

```csharp
// Pseudo-code for "Get Apps for User"
public async Task<List<WebApp>> GetAppsForUser(Guid userId)
{
    // 1. Get user's personal apps
    var personalApps = await _context.WebApps
        .Where(a => a.Scope == AccessScope.Personal &&
                    a.WebAppUsers.Any(wu => wu.UserId == userId))
        .ToListAsync();
    
    // 2. Get user's project memberships
    var projectIds = await _context.ProjectMembers
        .Where(pm => pm.UserId == userId && pm.IsActive)
        .Select(pm => pm.ProjectId)
        .ToListAsync();
    
    // 3. Get apps from those projects (filtered by role)
    var projectApps = await _context.WebApps
        .Where(a => a.Scope == AccessScope.Project &&
                    a.ProjectApps.Any(pa => projectIds.Contains(pa.ProjectId)))
        .ToListAsync();
    
    // 4. Filter by role permissions
    var filteredProjectApps = new List<WebApp>();
    foreach (var app in projectApps)
    {
        var projectId = app.ProjectApps.First().ProjectId;
        var member = await GetProjectMember(userId, projectId);
        
        if (member != null && CanAccessApp(member.Role))
        {
            filteredProjectApps.Add(app);
        }
    }
    
    // 5. Combine and return
    return personalApps.Concat(filteredProjectApps).ToList();
}

private bool CanAccessApp(string role)
{
    return role switch
    {
        ProjectRole.ProjectAdmin => true,
        ProjectRole.Developer => true,
        ProjectRole.AppUser => true,
        _ => false
    };
}
```

---

## API Design

### New API Endpoints

#### Project Management

```
POST   /api/projects                        Create new project
GET    /api/projects                        List user's projects
GET    /api/projects/{id}                   Get project details
PUT    /api/projects/{id}                   Update project
DELETE /api/projects/{id}                   Delete project
GET    /api/projects/{id}/stats             Get project statistics
```

#### Project Members

```
POST   /api/projects/{id}/members           Add member to project
GET    /api/projects/{id}/members           List project members
PUT    /api/projects/{id}/members/{userId}  Update member role
DELETE /api/projects/{id}/members/{userId}  Remove member
POST   /api/projects/{id}/invite            Send invitation email
```

#### Project Resources

```
POST   /api/projects/{id}/apps/{appId}      Add app to project
DELETE /api/projects/{id}/apps/{appId}      Remove app from project
GET    /api/projects/{id}/apps              List project apps

POST   /api/projects/{id}/flows/{flowId}    Add flow to project
DELETE /api/projects/{id}/flows/{flowId}    Remove flow from project
GET    /api/projects/{id}/flows             List project flows

POST   /api/projects/{id}/runners/{runnerId} Add runner to project
DELETE /api/projects/{id}/runners/{runnerId} Remove runner
GET    /api/projects/{id}/runners            List project runners
```

#### User Context

```
GET    /api/user/context                    Get current user's project context
PUT    /api/user/context/project/{id}       Switch to project
```

### Modified Endpoints

Existing endpoints need to support project-scoped queries:

```
GET /api/apps?projectId={id}               Filter by project
GET /api/flows?projectId={id}              Filter by project
GET /api/runners?projectId={id}            Filter by project
```

### Example API Contracts

#### Create Project
```json
POST /api/projects
{
  "name": "engineering-tools",
  "displayName": "Engineering Tools",
  "description": "Internal automation tools for engineering team"
}

Response 201:
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "name": "engineering-tools",
  "displayName": "Engineering Tools",
  "description": "Internal automation tools for engineering team",
  "ownerId": "user-id",
  "created": "2024-11-09T10:00:00Z",
  "memberCount": 1
}
```

#### Add Member
```json
POST /api/projects/{projectId}/members
{
  "email": "developer@example.com",
  "role": "Developer",
  "sendInvite": true
}

Response 201:
{
  "userId": "new-user-id",
  "email": "developer@example.com",
  "role": "Developer",
  "joinedAt": "2024-11-09T10:05:00Z",
  "invitationSent": true
}
```

#### List Project Apps
```json
GET /api/projects/{projectId}/apps

Response 200:
{
  "apps": [
    {
      "id": "app-id-1",
      "name": "dashboard",
      "displayName": "Team Dashboard",
      "isPublished": true,
      "addedAt": "2024-11-01T10:00:00Z",
      "addedBy": "user-id"
    }
  ]
}
```

---

## UI/UX Changes

### Navigation Changes

#### Current Main Menu
```
Home | Apps | Flows | Runners | Steps | API Keys | AI Keys | Settings | Downloads | Docs
```

#### New Main Menu (Enterprise Mode)
```
Home | Projects | Apps | Flows | Runners | Steps | API Keys | AI Keys | Settings | Downloads | Docs

[NEW] Projects dropdown:
  - Personal Workspace
  - [Project A] Engineering Tools
  - [Project B] Marketing Automation
  - Create New Project...
```

### Dashboard Personalization

#### Standard User Dashboard (Current)
Shows:
- Recent activity
- Quick actions: Create App, Create Flow, Add Runner
- Statistics: Apps, Flows, Executions

#### Developer Dashboard (Project Context)
Shows:
- **Project selector** at top
- Development tools section
- Project apps/flows/runners
- Quick actions: Create App, Create Flow
- Project activity feed

#### App User Dashboard (Project Context)
Shows:
- **App cards** (large, visual)
- No development tools
- Search/filter apps
- Favorites
- Usage statistics

Layout:
```
┌─────────────────────────────────────────────┐
│  [Project: Engineering Tools ▼]             │
├─────────────────────────────────────────────┤
│                                              │
│   ┌──────────┐  ┌──────────┐  ┌──────────┐│
│   │  📱 App  │  │  📱 App  │  │  📱 App  ││
│   │  Sales   │  │ Inventory│  │ Reports  ││
│   │  CRM     │  │ Tracker  │  │ Dashboard││
│   └──────────┘  └──────────┘  └──────────┘│
│                                              │
│   ┌──────────┐  ┌──────────┐               │
│   │  📱 App  │  │  📱 App  │               │
│   │ Employee │  │ Customer │               │
│   │ Portal   │  │ Support  │               │
│   └──────────┘  └──────────┘               │
│                                              │
└─────────────────────────────────────────────┘
```

### New Pages

#### 1. Projects Page (`/projects`)
Location: `src/Automaton.Studio/Pages/Projects/ProjectsPage.razor`

Features:
- List all projects user is member of
- Create new project button
- Cards showing:
  - Project name, description
  - Member count
  - Resource counts (apps, flows, runners)
  - Role badge (Admin, Developer, etc.)
  - Last activity

#### 2. Project Settings Page (`/projects/{id}/settings`)
Location: `src/Automaton.Studio/Pages/Projects/ProjectSettingsPage.razor`

Tabs:
- **General**: Name, description, logo
- **Members**: List members, add/remove, change roles
- **Resources**: Link apps/flows/runners to project
- **Access Control**: Configure default permissions
- **Billing**: (Future) Subscription info

#### 3. Project Members Management
Location: `src/Automaton.Studio/Pages/Projects/Components/MembersPanel.razor`

Features:
- Table: Email, Name, Role, Joined Date, Actions
- Add member modal:
  - Email input (if not exists, create account)
  - Role selector
  - Send invitation checkbox
- Role change dropdown (Admin only)
- Remove member button (Admin only)

#### 4. User Invitation Flow
Location: `src/Automaton.Studio/Pages/Projects/AcceptInvitation.razor`

Steps:
1. User receives email: "You've been invited to [Project Name]"
2. Click link → `/projects/invite/{token}`
3. If no account: Create account + set password
4. If existing account: Just accept
5. Redirect to project dashboard

### Modified Pages

#### Apps Page
Add project filter dropdown:
```razor
<Select @bind-Value="selectedProjectId">
    <SelectOption Value="null">Personal Apps</SelectOption>
    @foreach (var project in userProjects)
    {
        <SelectOption Value="@project.Id">@project.DisplayName</SelectOption>
    }
</Select>
```

When creating/editing app:
- Show "Share with Project" section
- Multi-select projects to add app to
- Set access scope: Personal / Project / Public

#### WebApp Viewer
Modify access check:
```csharp
// OLD: RequiresAuthentication = any authenticated user
// NEW: Check project membership if Scope = Project

if (app.Scope == AccessScope.Project && app.ProjectId.HasValue)
{
    var isMember = await ProjectAuthService.CanAccessProject(userId, app.ProjectId.Value);
    if (!isMember)
    {
        return Forbid();
    }
}
```

#### Settings Page
Add new "Projects & Organization" section (Enterprise users only):
- Default project
- Invitation settings
- Project creation permissions

---

## Migration Strategy

### Phase 1: Database Migration
**Goal:** Add new tables without breaking existing functionality

Steps:
1. Generate EF Core migration: `Add-Migration AddProjectEntities`
2. Tables created: `Projects`, `ProjectMembers`, `ProjectApps`, `ProjectFlows`, `ProjectRunners`, `ProjectCustomSteps`
3. Columns added: `WebApps.Scope`, `WebApps.ProjectId`, `ApplicationUser.CurrentProjectId`
4. Deploy to staging, run migration
5. Verify existing queries still work (backward compatibility)

### Phase 2: Feature Flag Rollout
**Goal:** Enable Projects for specific users (opt-in)

Implementation:
```csharp
// Location: src/Automaton.Studio.Server/Entities/ApplicationUser.cs
public class ApplicationUser
{
    // ...
    public bool IsEnterpriseEnabled { get; set; } = false;
}
```

UI:
- Show "Projects" menu item only if `IsEnterpriseEnabled`
- Add admin toggle: `/settings/enterprise` (SysAdmin only)

### Phase 3: Data Migration (Optional)
**Goal:** Move existing resources into projects for early adopters

Script:
```csharp
// Run once per user who enables Enterprise
public async Task MigrateUserToProjects(Guid userId)
{
    // 1. Create "Personal" project
    var project = new Project
    {
        Id = Guid.NewGuid(),
        Name = $"personal-{userId}",
        DisplayName = "Personal Workspace",
        OwnerId = userId,
        Created = DateTime.UtcNow,
        Updated = DateTime.UtcNow
    };
    
    await _context.Projects.AddAsync(project);
    
    // 2. Add user as ProjectAdmin
    var member = new ProjectMember
    {
        ProjectId = project.Id,
        UserId = userId,
        Role = ProjectRole.ProjectAdmin,
        JoinedAt = DateTime.UtcNow
    };
    
    await _context.ProjectMembers.AddAsync(member);
    
    // 3. Link existing apps
    var userApps = await _context.WebApps
        .Where(a => a.WebAppUsers.Any(wu => wu.UserId == userId))
        .ToListAsync();
    
    foreach (var app in userApps)
    {
        app.Scope = AccessScope.Project;
        app.ProjectId = project.Id;
        
        await _context.ProjectApps.AddAsync(new ProjectApp
        {
            ProjectId = project.Id,
            WebAppId = app.Id,
            AddedAt = DateTime.UtcNow,
            AddedBy = userId
        });
    }
    
    // 4. Similar for Flows, Runners, Steps
    
    await _context.SaveChangesAsync();
}
```

### Phase 4: UI Rollout
**Goal:** Release UI changes gradually

1. **Alpha (Internal)**: Test with Automaton developers
2. **Beta (Opt-in)**: Enterprise customers can enable via settings
3. **General Availability**: All users see Projects, but need Enterprise subscription to create/use

---

## Implementation Phases

### Phase 1: Core Infrastructure (Weeks 1-2)
**Focus:** Database, backend services, no UI

Deliverables:
- ✅ Entity models: `Project`, `ProjectMember`, linking tables
- ✅ EF Core migration
- ✅ Base API endpoints: CRUD projects, members
- ✅ Authorization handlers: Policy-based auth
- ✅ Unit tests for services
- ✅ Integration tests for API

**Exit Criteria:**
- Migration runs successfully on staging
- API endpoints return expected data
- Authorization prevents unauthorized access
- 80%+ test coverage

### Phase 2: Project Management UI (Weeks 3-4)
**Focus:** Project list, settings, member management

Deliverables:
- ✅ `/projects` page: List projects
- ✅ `/projects/{id}/settings` page
- ✅ Member management panel
- ✅ Invitation flow (email + accept page)
- ✅ Project switcher in main menu
- ✅ Feature flag: `IsEnterpriseEnabled`

**Exit Criteria:**
- Admin can create project
- Admin can invite users
- Users receive email and can accept
- Project appears in project list

### Phase 3: Resource Sharing (Weeks 5-6)
**Focus:** Link Apps/Flows/Runners to projects

Deliverables:
- ✅ API endpoints: Add/remove resources from projects
- ✅ Modified Apps page: Project filter, "Add to Project"
- ✅ Modified Flows page: Project filter
- ✅ Modified Runners page: Project context
- ✅ Access control: Enforce project permissions

**Exit Criteria:**
- Admin can add App to project
- Developer in project can see/edit the app
- AppUser can only execute the app
- Non-members cannot see project resources

### Phase 4: Dashboard Personalization (Weeks 7-8)
**Focus:** Role-based dashboards, UX polish

Deliverables:
- ✅ AppUser dashboard: Card-based app portal
- ✅ FlowUser dashboard: Execute-only view
- ✅ Developer dashboard: Full tools + project context
- ✅ Statistics: Project-level analytics
- ✅ Onboarding guide: "Get Started with Projects"

**Exit Criteria:**
- AppUser sees only apps (no dev tools)
- Dashboard changes based on role
- Smooth UX: No confusion about what user can do

### Phase 5 (Future): Advanced Features
**Focus:** Enhancements beyond MVP

Ideas:
- Project templates ("Marketing Automation", "Sales Tools")
- Resource sharing across projects
- Project-level API keys
- Audit logs (who did what in project)
- Project cloning
- Advanced RBAC (custom roles)
- Project billing/usage tracking

---

## Security Considerations

### 1. Data Isolation
**Risk:** User in Project A accesses resources from Project B

**Mitigation:**
- Always validate `ProjectId` in authorization handlers
- Join queries: Ensure user is member before returning data
- Row-level security: Filter by `ProjectId` + `UserId`
- Logging: Audit all cross-project access attempts

### 2. Privilege Escalation
**Risk:** AppUser elevates to Developer role

**Mitigation:**
- Only ProjectAdmin can change roles
- API validates caller's role before allowing operation
- UI hides privileged actions (defense in depth)
- Logging: Audit all role changes

### 3. Invitation Abuse
**Risk:** Attacker creates accounts for arbitrary emails

**Mitigation:**
- Rate limit invitations (e.g., 10 per hour per project)
- Require email verification
- Invitation tokens expire (24 hours)
- Log all invitation attempts
- Option: Require admin approval for new accounts

### 4. Project Owner Deletion
**Risk:** Owner leaves, project becomes orphaned

**Mitigation:**
- Require at least one ProjectAdmin before owner can leave
- Transfer ownership flow
- Auto-promote admin if owner is deleted
- Soft-delete projects (retain data)

### 5. Public vs Protected WebApps
**Risk:** User accidentally exposes sensitive app

**Mitigation:**
- Clear UI warnings when publishing publicly
- Confirmation dialog: "This app will be accessible to anyone"
- Audit log: Track publish actions
- Option: Require admin approval for public apps

### 6. API Key Scoping
**Risk:** API key for App execution used to access admin endpoints

**Mitigation:**
- Add `ProjectId` scope to API keys
- Key can only access resources in its project
- Document clearly: "WebApp" scope != "FullAccess"
- Monitoring: Alert on suspicious key usage

---

## Testing Strategy

### Unit Tests
Location: `src/UnitTests/Automaton.Studio.Server.Tests/`

Test Coverage:
- **ProjectAuthorizationService**: Role checks, membership validation
- **ProjectMemberHandler**: Policy authorization logic
- **ProjectService**: CRUD operations, validation
- **Resource linking**: Add/remove apps/flows from projects

Example:
```csharp
[Fact]
public async Task User_CannotAccess_OtherProjectResources()
{
    // Arrange
    var project1 = CreateProject("Project A");
    var project2 = CreateProject("Project B");
    var user = CreateUser();
    AddUserToProject(user, project1, ProjectRole.Developer);
    
    var app = CreateApp(project2);
    
    // Act
    var canAccess = await _authService.CanAccessApp(user.Id, app.Id);
    
    // Assert
    Assert.False(canAccess);
}
```

### Integration Tests
Location: `src/UnitTests/Automaton.Studio.Server.Tests/Integration/`

Test Scenarios:
- **Project Creation**: POST `/api/projects` creates project + owner membership
- **Member Invitation**: POST invite → Email sent → Accept link works
- **Resource Sharing**: Add app to project → Members can access
- **Authorization**: Non-member GET `/api/projects/{id}` → 403 Forbidden

### End-to-End Tests
Use Playwright or Selenium:

Test Flows:
1. **Admin Creates Project**
   - Login as admin
   - Navigate to `/projects`
   - Click "Create Project"
   - Fill form, submit
   - Verify project appears in list

2. **Invite Developer**
   - Open project settings
   - Go to Members tab
   - Enter email, select "Developer" role
   - Click "Invite"
   - Verify email sent

3. **AppUser Dashboard**
   - Login as app user
   - Verify no "Apps" menu item
   - Verify dashboard shows only app cards
   - Click app → Opens successfully

### Security Tests
- **Authorization bypass**: Try accessing project without membership
- **Role escalation**: Try changing own role via API
- **CSRF protection**: Ensure anti-forgery tokens work
- **SQL injection**: Test with malicious input
- **XSS**: Ensure project names/descriptions are sanitized

---

## Open Questions & Discussion Points

### 1. Backward Compatibility: Automatic Migration or Opt-In?

**Option A: Opt-In (Recommended)**
- Existing users stay in "Standard Mode"
- Must explicitly enable "Projects" feature
- Resources remain personal

**Option B: Automatic Migration**
- Create "Personal Project" for each user
- Migrate all resources into it
- Unified codebase (always project-scoped)

**Question:** Which approach is better for user experience?

### 2. Subscription Model

**Where to enforce?**
- API level: Return 403 if not Enterprise subscriber?
- UI level: Hide features for non-subscribers?
- Grace period: Allow X days trial?

**Billing Integration:**
- Integrate with Stripe/Chargebee?
- Manual invoicing for now?

### 3. Personal Workspace + Projects

**Should users have BOTH?**
- **Yes**: Personal workspace for personal projects, Projects for teams
- **No**: Once you create a Project, everything goes into projects

**Recommendation:** Yes, allow both. Users can keep personal experiments separate.

### 4. Project Naming

**What to call it?**
- "Project"
- "Organization"
- "Workspace"
- "Team"

**Recommendation:** "Project" is clearer for single-team scenarios. "Organization" implies multiple teams. Start with "Project", allow orgs to have multiple projects.

### 5. Resource Ownership

**Who owns a resource in a project?**
- **Option A:** Project owns it (no individual owner)
- **Option B:** Creator owns it, but shared with project

**Implications:**
- If creator leaves project, does resource stay?
- Can creator move resource to another project?

**Recommendation:** Project owns it. Creator has `CreatedBy` field for audit, but resource stays with project.

### 6. Multi-Project Membership

**Can an App be in multiple projects?**
- **Yes:** Share "Customer Portal" app across Sales + Support projects
- **No:** Each app belongs to exactly one project

**Recommendation:** Phase 1 = single project. Phase 2+ = multi-project sharing.

### 7. Default Project

**When user logs in, which project context?**
- **Option A:** Last used project (store in `CurrentProjectId`)
- **Option B:** Always start in "Personal Workspace"
- **Option C:** Project switcher in header (no default)

**Recommendation:** Option A (last used) with Option C (always visible switcher).

### 8. Invitation Workflow

**Create account immediately or wait for acceptance?**
- **Option A:** Create account + send invite email → User sets password
- **Option B:** Send invite link → User creates account during acceptance

**Pros/Cons:**
- **A:** Admin sees user in list immediately. Con: Orphaned accounts if never accepted.
- **B:** Cleaner (no orphans). Con: Admin can't see "pending invites".

**Recommendation:** Option A with "Pending" status. Add cleanup job to delete accounts after 7 days if not accepted.

### 9. Role Granularity

**Should we have more fine-grained permissions?**
- Current: ProjectAdmin, Developer, AppUser, FlowUser, Viewer
- Future: Custom roles? (e.g., "App Editor" who can edit but not create)

**Recommendation:** Start simple (5 roles). Phase 2+ add custom roles.

### 10. Runner Assignment

**Can runners be project-specific?**
- **Scenario:** Each project has dedicated runners (different infrastructure)
- **Alternative:** Runners are global, projects just tag them

**Recommendation:** Runners are project-scoped. Each project registers its own runners. Prevents resource contention.

---

## Appendix: Comparison to Industry Standards

### Slack
- **Workspaces** = Projects
- **Channels** = Not applicable (Automaton has Apps/Flows)
- **Roles**: Owner, Admin, Member, Guest
- **Billing**: Per workspace

**Takeaway:** Simple 4-role model works well.

### GitHub Organizations
- **Organizations** = Projects
- **Repositories** = Apps/Flows
- **Roles**: Owner, Member, Outside Collaborator
- **Permissions**: Read, Write, Admin (per repo)

**Takeaway:** Separate org-level roles from resource-level permissions.

### Jira/Confluence (Atlassian)
- **Projects** = Projects
- **Roles**: Project Admin, Developer, User
- **Multi-project membership** common

**Takeaway:** Users often belong to 5-10 projects. Need efficient switcher.

### Figma
- **Teams** = Projects
- **Files** = Apps/Flows
- **Roles**: Owner, Admin, Editor, Viewer
- **Billing**: Per team

**Takeaway:** Clear distinction between Editors (creators) and Viewers (consumers).

---

## Conclusion

This Technical Approach Document outlines a comprehensive strategy for implementing **Project/Organization** multi-tenancy in Automaton Studio. The phased approach ensures:

✅ **Backward compatibility** with existing users
✅ **Clear separation** between development tools and end-user portals
✅ **Enterprise readiness** for B2B SaaS model
✅ **Scalable architecture** using industry best practices
✅ **Security-first design** with robust access control

**Next Steps:**
1. Review and approve this TAD
2. Prioritize Phase 1 (Core Infrastructure)
3. Set up project board and assign tasks
4. Begin implementation with database schema

**Estimated Timeline:**
- Phase 1 (Core): 2 weeks
- Phase 2 (UI): 2 weeks
- Phase 3 (Resources): 2 weeks
- Phase 4 (Dashboards): 2 weeks
- **Total MVP: 8 weeks**

---

**Document Version:** 1.0  
**Author:** Automaton Team  
**Date:** November 9, 2024  
**Status:** Draft for Review
