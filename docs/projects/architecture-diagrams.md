# Project/Organization Feature - Architecture Diagrams

This document provides visual representations of the Project/Organization architecture to complement the [Technical Approach Document](./project-organization-tad.md).

---

## System Overview

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Automaton Platform                               │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                           │
│  ┌──────────────────────┐              ┌────────────────────────────┐  │
│  │   Standard User      │              │   Enterprise User          │  │
│  │   (Solo Creator)     │              │   (Project Owner)          │  │
│  └─────────┬────────────┘              └──────────┬─────────────────┘  │
│            │                                       │                     │
│            │ Creates/Uses                          │ Creates             │
│            │                                       │                     │
│            ▼                                       ▼                     │
│  ┌──────────────────────┐              ┌────────────────────────────┐  │
│  │ Personal Workspace   │              │      PROJECT(S)            │  │
│  │ ├─ Apps              │              │  ┌─────────────────────┐  │  │
│  │ ├─ Flows             │              │  │ Project: Marketing  │  │  │
│  │ ├─ Steps             │              │  │ ├─ Members (15)     │  │  │
│  │ └─ Runners           │              │  │ ├─ Apps (8)         │  │  │
│  └──────────────────────┘              │  │ ├─ Flows (20)       │  │  │
│                                         │  │ └─ Runners (3)      │  │  │
│                                         │  └─────────────────────┘  │  │
│                                         │  ┌─────────────────────┐  │  │
│                                         │  │ Project: Sales      │  │  │
│                                         │  │ ├─ Members (10)     │  │  │
│                                         │  │ ├─ Apps (5)         │  │  │
│                                         │  │ └─ Flows (12)       │  │  │
│                                         │  └─────────────────────┘  │  │
│                                         └────────────────────────────┘  │
│                                                                           │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Data Model

### Entity Relationship Diagram

```
┌─────────────────────────┐
│   ApplicationUser       │
├─────────────────────────┤
│ Id (PK)                 │◄──────────┐
│ Name                    │           │
│ Email                   │           │
│ IsEnterpriseEnabled     │           │ Owner
│ CurrentProjectId (FK)?  │◄────┐     │
└──────────┬──────────────┘     │     │
           │                    │     │
           │ Has Many           │     │
           │ Memberships        │     │
           ▼                    │     │
┌─────────────────────────┐    │     │
│    ProjectMember        │    │     │
├─────────────────────────┤    │     │
│ ProjectId (PK, FK)      │────┤     │
│ UserId (PK, FK)         │    │     │
│ Role                    │    │     │
│ JoinedAt                │    │     │
│ IsActive                │    │     │
└──────────┬──────────────┘    │     │
           │                    │     │
           │ Belongs To         │     │
           ▼                    │     │
┌─────────────────────────┐    │     │
│       Project           │────┘     │
├─────────────────────────┤          │
│ Id (PK)                 │──────────┘
│ Name (Unique)           │
│ DisplayName             │
│ OwnerId (FK)            │
│ Created                 │
│ IsActive                │
└──────────┬──────────────┘
           │
           │ Has Many
           │ Resources
           ▼
┌─────────────────────────┐         ┌─────────────────────────┐
│     ProjectApp          │         │      WebApp             │
├─────────────────────────┤         ├─────────────────────────┤
│ ProjectId (PK, FK)      │────────►│ Id (PK)                 │
│ WebAppId (PK, FK)       │         │ Name                    │
│ AddedAt                 │         │ DisplayName             │
│ AddedBy (FK)            │         │ Scope (enum)            │
└─────────────────────────┘         │ ProjectId (FK)?         │
                                    └─────────────────────────┘

┌─────────────────────────┐         ┌─────────────────────────┐
│     ProjectFlow         │         │        Flow             │
├─────────────────────────┤         ├─────────────────────────┤
│ ProjectId (PK, FK)      │────────►│ Id (PK)                 │
│ FlowId (PK, FK)         │         │ Name                    │
│ AddedAt                 │         │ Body                    │
│ AddedBy (FK)            │         └─────────────────────────┘
└─────────────────────────┘

┌─────────────────────────┐         ┌─────────────────────────┐
│    ProjectRunner        │         │       Runner            │
├─────────────────────────┤         ├─────────────────────────┤
│ ProjectId (PK, FK)      │────────►│ Id (PK)                 │
│ RunnerId (PK, FK)       │         │ Name                    │
│ AddedAt                 │         │ Status                  │
│ AddedBy (FK)            │         └─────────────────────────┘
└─────────────────────────┘
```

### Access Scope Flow

```
┌────────────────────────────────────────────────────────────┐
│                    WebApp / Flow                            │
├────────────────────────────────────────────────────────────┤
│                                                             │
│  Scope = Personal                                           │
│  ├─ Linked to User via WebAppUser/FlowUser                 │
│  ├─ Only accessible by specific users                      │
│  └─ Backward compatible (existing behavior)                │
│                                                             │
│  Scope = Project                                            │
│  ├─ Linked to Project via ProjectApp/ProjectFlow           │
│  ├─ Accessible by project members (role-based)             │
│  └─ ProjectId field is populated                           │
│                                                             │
│  Scope = Public                                             │
│  ├─ Published to public URL                                │
│  ├─ No authentication required                             │
│  └─ Accessible to anyone                                   │
│                                                             │
└────────────────────────────────────────────────────────────┘
```

---

## User Flows

### Project Creation & Member Invitation

```
┌─────────────┐
│ Project     │
│ Owner       │
└──────┬──────┘
       │
       │ 1. Creates Project
       ▼
┌─────────────────────────────────┐
│ POST /api/projects              │
│ {                               │
│   name: "engineering-tools"     │
│   displayName: "Engineering"    │
│ }                               │
└──────────────┬──────────────────┘
               │
               │ 2. Project Created
               │    Owner → ProjectAdmin
               ▼
┌─────────────────────────────────┐
│ Projects Table                  │
│ + ProjectMembers Table          │
│   (Owner = ProjectAdmin)        │
└──────────────┬──────────────────┘
               │
               │ 3. Invites Developer
               ▼
┌─────────────────────────────────┐
│ POST /api/projects/{id}/members │
│ {                               │
│   email: "dev@company.com"      │
│   role: "Developer"             │
│ }                               │
└──────────────┬──────────────────┘
               │
               │ 4. Check if user exists
               ▼
       ┌───────────────┐
       │ User Exists?  │
       └───┬───────┬───┘
           │       │
      YES  │       │ NO
           │       │
           ▼       ▼
    ┌──────────┐ ┌─────────────────┐
    │ Add to   │ │ Create account  │
    │ Project  │ │ + Send invite   │
    └────┬─────┘ └────┬────────────┘
         │            │
         │            │ 5. Email sent
         │            ▼
         │     ┌──────────────────────┐
         │     │ User receives email  │
         │     │ with invite link     │
         │     └──────────┬───────────┘
         │                │
         │                │ 6. Clicks link
         │                ▼
         │     ┌──────────────────────┐
         │     │ Sets password        │
         │     │ + Accepts invite     │
         │     └──────────┬───────────┘
         │                │
         └────────┬───────┘
                  │
                  │ 7. Member added
                  ▼
       ┌────────────────────────┐
       │ ProjectMembers Table   │
       │ + User can access      │
       │   project resources    │
       └────────────────────────┘
```

### Resource Access Authorization

```
┌─────────────┐
│   User      │
└──────┬──────┘
       │
       │ Requests: GET /api/projects/{projectId}/apps/{appId}
       ▼
┌─────────────────────────────────────────┐
│         Authorization Check              │
└──────────────┬──────────────────────────┘
               │
               │ Step 1: Is user a member?
               ▼
       ┌───────────────────┐
       │ ProjectMember     │
       │ WHERE             │
       │   ProjectId = X   │
       │   UserId = Y      │
       │   IsActive = true │
       └───────┬───────────┘
               │
       ┌───────┴────────┐
       │                │
    YES│                │NO
       │                │
       ▼                ▼
┌────────────┐   ┌────────────┐
│ Continue   │   │ Return 403 │
└──────┬─────┘   │ Forbidden  │
       │         └────────────┘
       │
       │ Step 2: Check role permission
       ▼
┌─────────────────────────────┐
│ User's Role in Project?     │
└──────┬──────────────────────┘
       │
       ├─ ProjectAdmin → ✅ Allow (all permissions)
       ├─ Developer    → ✅ Allow (can access apps)
       ├─ AppUser      → ✅ Allow (can access apps)
       ├─ FlowUser     → ❌ Deny (apps not allowed)
       └─ Viewer       → 🔍 Read-only (depends on action)
       │
       ▼
┌─────────────────────────────┐
│ Step 3: Resource ownership? │
└──────┬──────────────────────┘
       │
       │ Is app in this project?
       ▼
┌─────────────────────────────┐
│ ProjectApp                  │
│ WHERE                       │
│   ProjectId = X             │
│   WebAppId = Z              │
└───────┬─────────────────────┘
        │
    ┌───┴───┐
    │       │
 YES│       │NO
    │       │
    ▼       ▼
┌─────┐ ┌─────────┐
│ ✅  │ │ ❌ 400  │
│Allow│ │ Bad Req │
└─────┘ └─────────┘
```

---

## UI Flow

### Dashboard Personalization by Role

```
┌────────────────────────────────────────────────────────────────┐
│                      User Logs In                               │
└───────────────────────┬────────────────────────────────────────┘
                        │
                        │ Load user's projects & roles
                        ▼
                ┌───────────────────┐
                │ User has projects?│
                └────────┬──────────┘
                         │
                    ┌────┴────┐
                    │         │
                 YES│         │NO
                    │         │
                    ▼         ▼
        ┌────────────────┐ ┌──────────────────┐
        │ Get current    │ │ Show standard    │
        │ project + role │ │ dashboard        │
        └────────┬───────┘ │ (Personal mode)  │
                 │         └──────────────────┘
                 │
    ┌────────────┼────────────┐
    │            │            │
    ▼            ▼            ▼
┌─────────┐ ┌─────────┐ ┌─────────┐
│Project  │ │Developer│ │AppUser  │
│Admin    │ │         │ │         │
└────┬────┘ └────┬────┘ └────┬────┘
     │           │           │
     │           │           │
     ▼           ▼           ▼
┌─────────────────────────────────────────────────────────┐
│                Dashboard Rendering                       │
├─────────────────────────────────────────────────────────┤
│                                                          │
│ ProjectAdmin / Developer:                               │
│ ┌────────────────────────────────────────────────────┐ │
│ │ Navigation: Home | Projects | Apps | Flows |       │ │
│ │             Runners | Steps | API Keys | ...       │ │
│ ├────────────────────────────────────────────────────┤ │
│ │ Dashboard:                                          │ │
│ │ ┌────────────────┐  ┌────────────────┐           │ │
│ │ │ Quick Actions  │  │ Project Stats  │           │ │
│ │ │ • Create App   │  │ • 8 Apps       │           │ │
│ │ │ • Create Flow  │  │ • 20 Flows     │           │ │
│ │ └────────────────┘  │ • 3 Runners    │           │ │
│ │                     └────────────────┘           │ │
│ │ ┌──────────────────────────────────────────────┐ │ │
│ │ │ Recent Activity                               │ │ │
│ │ │ • Alice created "Dashboard" app               │ │ │
│ │ │ • Bob executed "Report" flow                  │ │ │
│ │ └──────────────────────────────────────────────┘ │ │
│ └────────────────────────────────────────────────────┘ │
│                                                          │
│ AppUser:                                                 │
│ ┌────────────────────────────────────────────────────┐ │
│ │ Navigation: Home | [Project Selector]              │ │
│ │             (No developer tools visible)           │ │
│ ├────────────────────────────────────────────────────┤ │
│ │ Dashboard (App Portal):                            │ │
│ │ ┌──────────┐  ┌──────────┐  ┌──────────┐         │ │
│ │ │ 📱 Sales │  │ 📱 CRM   │  │ 📱Report │         │ │
│ │ │ Dashboard│  │ Portal   │  │ Builder  │         │ │
│ │ └──────────┘  └──────────┘  └──────────┘         │ │
│ │ ┌──────────┐  ┌──────────┐                       │ │
│ │ │ 📱 Ticket│  │ 📱 Vendor│                       │ │
│ │ │ Tracker  │  │ Manager  │                       │ │
│ │ └──────────┘  └──────────┘                       │ │
│ └────────────────────────────────────────────────────┘ │
│                                                          │
└─────────────────────────────────────────────────────────┘
```

### Project Switcher Flow

```
┌────────────────────────────────────────────────┐
│  Header: [Project: Marketing ▼] [User Menu]   │
└────────────────┬───────────────────────────────┘
                 │
                 │ User clicks dropdown
                 ▼
        ┌─────────────────────┐
        │ Project Selector    │
        ├─────────────────────┤
        │ ✓ Marketing (Admin) │◄─── Current
        │   Sales (Developer) │
        │   Support (AppUser) │
        │ ───────────────────│
        │ + Create Project    │
        └──────────┬──────────┘
                   │
                   │ User selects "Sales"
                   ▼
        ┌─────────────────────────┐
        │ PUT /api/user/context   │
        │ { projectId: "..." }    │
        └──────────┬──────────────┘
                   │
                   │ Update CurrentProjectId
                   ▼
        ┌─────────────────────────┐
        │ Reload Dashboard        │
        │ + Navigation Menu       │
        │ (based on new role)     │
        └─────────────────────────┘
```

---

## Authorization Flow

### Policy-Based Authorization

```
┌──────────────────────────────────────────────────────────────┐
│                  API Endpoint Called                          │
│  [Authorize(Policy = "ProjectDeveloper")]                    │
│  POST /api/projects/{projectId}/apps                         │
└──────────────────────┬───────────────────────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────────────────────┐
│         ASP.NET Core Authorization Pipeline                   │
└──────────────────────┬───────────────────────────────────────┘
                       │
                       │ Execute policy requirement
                       ▼
┌──────────────────────────────────────────────────────────────┐
│         ProjectMemberRequirement Handler                      │
├──────────────────────────────────────────────────────────────┤
│                                                               │
│  1. Extract UserId from ClaimsPrincipal                       │
│  2. Extract ProjectId from route params                       │
│  3. Query database:                                           │
│                                                               │
│     SELECT * FROM ProjectMembers                              │
│     WHERE ProjectId = @projectId                              │
│       AND UserId = @userId                                    │
│       AND IsActive = true                                     │
│                                                               │
│  4. Check if member.Role in AllowedRoles                      │
│     ["ProjectAdmin", "Developer"]                             │
│                                                               │
└──────────────────────┬───────────────────────────────────────┘
                       │
              ┌────────┴────────┐
              │                 │
          Success           Failure
              │                 │
              ▼                 ▼
    ┌─────────────────┐  ┌─────────────────┐
    │ context.Succeed │  │ context.Fail()  │
    └────────┬────────┘  └────────┬────────┘
             │                     │
             ▼                     ▼
    ┌─────────────────┐  ┌─────────────────┐
    │ Execute         │  │ Return 403      │
    │ Controller      │  │ Forbidden       │
    │ Action          │  └─────────────────┘
    └─────────────────┘
```

---

## Deployment Architecture

### Multi-Environment Setup

```
┌─────────────────────────────────────────────────────────────┐
│                     Development                              │
├─────────────────────────────────────────────────────────────┤
│  • Local SQL Server / SQLite                                │
│  • Feature flags: All enabled                               │
│  • Test data: Sample projects, users                        │
│  • Logging: Verbose                                         │
└─────────────────────────────────────────────────────────────┘
                            │
                            │ Deploy
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                      Staging                                 │
├─────────────────────────────────────────────────────────────┤
│  • Azure SQL / MySQL                                        │
│  • Feature flags: Enterprise opt-in only                    │
│  • Real-like data: 10-20 test projects                      │
│  • Logging: Info                                            │
│  • Testing: Integration tests, E2E tests                    │
└─────────────────────────────────────────────────────────────┘
                            │
                            │ Promote (after QA)
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                     Production                               │
├─────────────────────────────────────────────────────────────┤
│  • Azure SQL / MySQL (HA)                                   │
│  • Feature flags: Gradual rollout (5% → 20% → 100%)        │
│  • Real data: Customer projects                            │
│  • Logging: Warning/Error                                  │
│  • Monitoring: Application Insights, alerts                │
│  • Backups: Automated daily                                │
└─────────────────────────────────────────────────────────────┘
```

### Database Migration Flow

```
┌────────────────┐
│ EF Core        │
│ Migration      │
│ Generated      │
└───────┬────────┘
        │
        │ dotnet ef migrations add AddProjectEntities
        ▼
┌────────────────────────────────────┐
│ Migration File Created             │
│ • Up(): Create tables              │
│ • Down(): Rollback                 │
└───────┬────────────────────────────┘
        │
        │ Review manually
        ▼
┌────────────────────────────────────┐
│ Apply to Dev                       │
│ dotnet ef database update          │
└───────┬────────────────────────────┘
        │
        │ Test locally
        ▼
┌────────────────────────────────────┐
│ Apply to Staging                   │
│ (via CI/CD pipeline)               │
│ • Backup database first            │
│ • Run migration                    │
│ • Verify schema                    │
└───────┬────────────────────────────┘
        │
        │ QA approval
        ▼
┌────────────────────────────────────┐
│ Apply to Production                │
│ • Schedule maintenance window      │
│ • Backup database                  │
│ • Run migration                    │
│ • Monitor for errors               │
│ • Rollback plan ready              │
└────────────────────────────────────┘
```

---

## Performance Optimization

### Query Optimization Example

```
BAD (N+1 Query):
┌─────────────────────────────────────┐
│ Get user's projects                 │
└──────────┬──────────────────────────┘
           │
           │ SELECT * FROM ProjectMembers
           │ WHERE UserId = @userId
           ▼
┌─────────────────────────────────────┐
│ For each project:                   │
│   SELECT * FROM Projects            │ ◄── Executes N times!
│   WHERE Id = @projectId             │
└─────────────────────────────────────┘

GOOD (Single Query with Join):
┌─────────────────────────────────────┐
│ SELECT p.*, pm.Role                 │
│ FROM ProjectMembers pm              │
│ INNER JOIN Projects p               │
│   ON pm.ProjectId = p.Id            │
│ WHERE pm.UserId = @userId           │
│   AND pm.IsActive = true            │
└─────────────────────────────────────┘
```

### Caching Strategy

```
┌─────────────────────────────────────────────────────────┐
│               Request Flow with Cache                    │
└─────────────────┬───────────────────────────────────────┘
                  │
                  │ GET /api/projects
                  ▼
         ┌─────────────────┐
         │ Check cache:    │
         │ Key = "user_    │
         │  projects_{id}" │
         └────────┬────────┘
                  │
          ┌───────┴────────┐
          │                │
       Found           Not Found
          │                │
          ▼                ▼
   ┌────────────┐   ┌──────────────┐
   │ Return     │   │ Query DB     │
   │ from cache │   └──────┬───────┘
   └────────────┘          │
          │                │ Store in cache
          │                │ TTL = 5 min
          │                ▼
          │         ┌──────────────┐
          │         │ Return data  │
          │         └──────────────┘
          │                │
          └────────┬───────┘
                   │
                   ▼
          ┌────────────────┐
          │ Response sent  │
          └────────────────┘

Cache Invalidation:
┌─────────────────────────────────┐
│ POST /api/projects/{id}/members │
│ (Member added)                  │
└──────────┬──────────────────────┘
           │
           │ After DB update
           ▼
    ┌──────────────────┐
    │ cache.Remove(    │
    │  "user_projects_ │
    │   {userId}"      │
    │ )                │
    └──────────────────┘
```

---

**Document Version:** 1.0  
**Last Updated:** November 9, 2024  
**Maintained by:** Automaton Development Team
