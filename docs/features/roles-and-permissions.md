# Roles and Permissions in Automaton

This document explains how the role and permission system works in Automaton. Understanding the distinction between different types of roles is crucial for proper system usage.

## Two Separate Role Systems

Automaton uses **two independent role systems** that serve different purposes:

### 1. ASP.NET Identity Roles (System-Wide)

**Database Table:** `AspNetRoles` and `AspNetUserRoles`

**Purpose:** System-wide authentication and authorization for platform access.

**Roles:**
- **SYSADMIN** - Full system access, can manage all platform resources
- **ADMIN** - Administrative access to platform features
- **USER** - Standard authenticated user

**How They're Used:**
- Control access to system-wide features
- Not related to specific projects or organizations
- Assigned during user registration or by system administrators

**Important:** These roles are **NOT** the same as ProjectRole or OrganizationRole.

---

### 2. Application-Level Roles (Resource-Specific)

**Database Tables:** `ProjectMembers` and `OrganizationMembers`

**Purpose:** Fine-grained permissions within specific projects and organizations.

#### Organization Roles

**Database Table:** `OrganizationMembers.Role` (enum: `OrganizationRole`)

**Roles:**

| Role | Value | Permissions |
|------|-------|-------------|
| **OrgAdmin** | 0 | Full control over the organization including managing members, settings, and all projects. Can create projects, invite members, and assign apps to users. |
| **Developer** | 1 | Can create and manage projects within the organization. Full development access to own projects. |
| **AppUser** | 2 | Can only access apps that have been explicitly assigned. Consumer-focused role with simplified UI and no development tools visible. |

#### Project Roles

**Database Table:** `ProjectMembers.Role` (enum: `ProjectRole`)

**Roles:**

| Role | Value | Permissions |
|------|-------|-------------|
| **ProjectAdmin** | 0 | Full control over the project including managing members and settings. |
| **Developer** | 1 | Can create and edit apps, flows, steps, and runners. Full development access. |
| **AppUser** | 2 | Can only view and execute apps. Consumer-focused role. |
| **Viewer** | 3 | Read-only access to project resources. Can view but not execute or edit. |

---

## How Roles Are Assigned

### Automatic Role Assignment

When you create a resource, you automatically get admin privileges:

1. **Create a Project** → You become **ProjectAdmin**
   - Location: `ProjectService.CreateProjectAsync` (line 152-160)
   - Your membership is added to `ProjectMembers` table
   - Role: `ProjectRole.ProjectAdmin`

2. **Create an Organization** → You become **OrgAdmin**
   - Location: `OrganizationService.CreateOrganizationAsync` (line 141-149)
   - Your membership is added to `OrganizationMembers` table
   - Role: `OrganizationRole.OrgAdmin`

**Example:**
```csharp
// When you create a project, this happens automatically:
var ownerMember = new ProjectMember
{
    ProjectId = project.Id,
    UserId = currentUserId,
    Role = ProjectRole.ProjectAdmin,  // ← You automatically become ProjectAdmin
    JoinedAt = DateTime.UtcNow,
    IsActive = true
};
dbContext.ProjectMembers.Add(ownerMember);
```

### Manual Role Assignment

You can add other users to your projects/organizations with specific roles:

#### For Organizations:

1. **Via Invitation System:**
   - OrgAdmin can send email invitations
   - Specify the role when inviting (OrgAdmin, Developer, AppUser)
   - User accepts invitation via token-based link

2. **API Endpoint:** `POST /api/organizations/{id}/invitations`
   ```json
   {
     "email": "user@example.com",
     "role": 1  // 0=OrgAdmin, 1=Developer, 2=AppUser
   }
   ```

#### For Projects:

1. **Via Project Member Management:**
   - ProjectAdmin can add members directly
   - Assign roles when adding members

2. **API Endpoint:** Project member endpoints (via ProjectMemberService)

---

## How to Check Your Roles

### Query Organization Roles

```sql
-- See all organizations you're a member of and your role
SELECT 
    o.Name AS OrganizationName,
    om.Role AS YourRole,
    om.JoinedAt
FROM OrganizationMembers om
INNER JOIN Organizations o ON om.OrganizationId = o.Id
WHERE om.UserId = 'YOUR-USER-ID' AND om.IsActive = 1;
```

**Role Values:**
- 0 = OrgAdmin
- 1 = Developer
- 2 = AppUser

### Query Project Roles

```sql
-- See all projects you're a member of and your role
SELECT 
    p.Name AS ProjectName,
    pm.Role AS YourRole,
    pm.JoinedAt
FROM ProjectMembers pm
INNER JOIN Projects p ON pm.ProjectId = p.Id
WHERE pm.UserId = 'YOUR-USER-ID' AND pm.IsActive = 1;
```

**Role Values:**
- 0 = ProjectAdmin
- 1 = Developer
- 2 = AppUser
- 3 = Viewer

---

## Common Scenarios

### Scenario 1: I created a project but I'm not ProjectAdmin?

**Not Possible.** When you create a project, the system automatically adds you as ProjectAdmin. Check:

1. Is the project actually yours? Check `Projects.OwnerId` matches your user ID
2. Is your membership active? Check `ProjectMembers.IsActive = 1`
3. Look in the `ProjectMembers` table for your entry

### Scenario 2: I created an organization but I'm not OrgAdmin?

**Not Possible.** When you create an organization, the system automatically adds you as OrgAdmin. Check:

1. Is the organization actually yours? Check `Organizations.OwnerId` matches your user ID
2. Is your membership active? Check `OrganizationMembers.IsActive = 1`
3. Look in the `OrganizationMembers` table for your entry

### Scenario 3: I don't see ProjectAdmin or OrgAdmin in AspNetRoles table

**This is correct.** ProjectAdmin and OrgAdmin are NOT in the AspNetRoles table. They're stored in:
- `ProjectMembers.Role` (for ProjectAdmin)
- `OrganizationMembers.Role` (for OrgAdmin)

AspNetRoles only contains: SYSADMIN, ADMIN, USER.

### Scenario 4: I want to make someone else an OrgAdmin

**Use the UI or API:**

1. **Via UI:** Go to Organization → Members → Invite Member → Select "OrgAdmin" role
2. **Via API:** 
   ```bash
   POST /api/organizations/{organizationId}/invitations
   {
     "email": "user@example.com",
     "role": 0  # OrgAdmin
   }
   ```

### Scenario 5: I want to change someone's role

**Current Implementation:**
- Remove the member and re-invite with the new role
- Or manually update the database (not recommended for production)

**Database Update (Development Only):**
```sql
-- Change a user's organization role
UPDATE OrganizationMembers 
SET Role = 0  -- 0=OrgAdmin, 1=Developer, 2=AppUser
WHERE OrganizationId = 'ORG-ID' AND UserId = 'USER-ID';

-- Change a user's project role  
UPDATE ProjectMembers 
SET Role = 0  -- 0=ProjectAdmin, 1=Developer, 2=AppUser, 3=Viewer
WHERE ProjectId = 'PROJECT-ID' AND UserId = 'USER-ID';
```

---

## Authorization Flow

### How the System Checks Permissions

1. **User makes a request** (e.g., access a project)
2. **System identifies the user** (from JWT token)
3. **System queries membership tables:**
   - `ProjectMembers` for project access
   - `OrganizationMembers` for organization access
4. **System checks the role** and allows/denies the operation

**Example Authorization Check:**
```csharp
// Check if user is OrgAdmin
var membership = await dbContext.OrganizationMembers
    .FirstOrDefaultAsync(om => 
        om.OrganizationId == organizationId && 
        om.UserId == userId && 
        om.IsActive);

bool isOrgAdmin = membership?.Role == OrganizationRole.OrgAdmin;
```

---

## UI Behavior Based on Roles

### Organization Role UI Effects

**OrgAdmin:**
- Can see all organization views (Dashboard, Members, Projects, Apps, Settings)
- Can invite members
- Can link/unlink projects
- Can assign apps to AppUsers
- Can modify organization settings

**Developer:**
- Can see Dashboard, Projects, Apps
- Can create projects under the organization
- Cannot access Members or Settings
- Cannot assign apps

**AppUser:**
- Can only see Dashboard and "My Apps"
- Cannot see Projects, Members, Apps, or Settings
- Simplified navigation (hides development tools)

### Project Role UI Effects

**ProjectAdmin:**
- Full access to project management
- Can add/remove members
- Can modify project settings

**Developer:**
- Can create and edit resources
- Cannot manage members or settings

**AppUser:**
- Can only execute apps
- Read-only access to flows

**Viewer:**
- Read-only access to all resources
- Cannot execute or edit anything

---

## Development and Testing

### Setting Up Roles in Development

1. **Create resources to get admin roles:**
   ```
   - Create a project → You're ProjectAdmin
   - Create an organization → You're OrgAdmin
   ```

2. **Test different roles:**
   - Create a test user account
   - Invite them with specific roles
   - Accept the invitation
   - Test UI/API behavior

3. **Manual database updates (development only):**
   ```sql
   -- Make user an OrgAdmin of existing organization
   INSERT INTO OrganizationMembers (Id, OrganizationId, UserId, Role, JoinedAt, IsActive)
   VALUES (NEWID(), 'ORG-ID', 'USER-ID', 0, GETUTCDATE(), 1);
   
   -- Make user a ProjectAdmin of existing project
   INSERT INTO ProjectMembers (ProjectId, UserId, Role, JoinedAt, IsActive)
   VALUES ('PROJECT-ID', 'USER-ID', 0, GETUTCDATE(), 1);
   ```

---

## Summary

| Aspect | ASP.NET Identity Roles | Application-Level Roles |
|--------|------------------------|-------------------------|
| **Table** | AspNetRoles, AspNetUserRoles | ProjectMembers, OrganizationMembers |
| **Scope** | System-wide | Per-project, per-organization |
| **Roles** | SYSADMIN, ADMIN, USER | ProjectRole, OrganizationRole |
| **Purpose** | Platform access control | Resource-specific permissions |
| **Assignment** | By system admin / registration | Automatic on creation / invitation |
| **Query** | AspNetUserRoles table | ProjectMembers / OrganizationMembers |

**Key Takeaway:** If you're looking for ProjectAdmin or OrgAdmin roles, check the `ProjectMembers` and `OrganizationMembers` tables, NOT the `AspNetRoles` table.
