# How to Set Up User Roles

## Quick Answer

**ProjectAdmin and OrgAdmin roles are NOT in the `AspNetRoles` table.**

They're stored in:
- `ProjectMembers.Role` for ProjectAdmin
- `OrganizationMembers.Role` for OrgAdmin

## How to Become ProjectAdmin

**Option 1: Create a Project (Automatic)**
1. Go to Projects → Create Project
2. You're automatically made ProjectAdmin
3. Check the database:
   ```sql
   SELECT * FROM ProjectMembers 
   WHERE UserId = 'YOUR-USER-ID' AND ProjectId = 'YOUR-PROJECT-ID';
   -- Role = 0 means ProjectAdmin
   ```

**Option 2: Get Invited**
1. An existing ProjectAdmin invites you
2. They select ProjectAdmin as your role
3. You accept the invitation

**Option 3: Manual Database (Development Only)**
```sql
-- Add yourself as ProjectAdmin to an existing project
INSERT INTO ProjectMembers (ProjectId, UserId, Role, JoinedAt, IsActive)
VALUES (
    'PROJECT-ID-HERE',
    'YOUR-USER-ID-HERE',
    0,  -- 0 = ProjectAdmin
    GETUTCDATE(),
    1
);
```

## How to Become OrgAdmin

**Option 1: Create an Organization (Automatic)**
1. Go to Organizations → Create Organization
2. You're automatically made OrgAdmin
3. Check the database:
   ```sql
   SELECT * FROM OrganizationMembers 
   WHERE UserId = 'YOUR-USER-ID' AND OrganizationId = 'YOUR-ORG-ID';
   -- Role = 0 means OrgAdmin
   ```

**Option 2: Get Invited**
1. An existing OrgAdmin invites you
2. They select OrgAdmin as your role
3. You accept the invitation

**Option 3: Manual Database (Development Only)**
```sql
-- Add yourself as OrgAdmin to an existing organization
INSERT INTO OrganizationMembers (Id, OrganizationId, UserId, Role, InvitedBy, JoinedAt, IsActive)
VALUES (
    NEWID(),
    'ORG-ID-HERE',
    'YOUR-USER-ID-HERE',
    0,  -- 0 = OrgAdmin
    NULL,
    GETUTCDATE(),
    1
);
```

## Role Values Reference

### ProjectRole (ProjectMembers.Role)
- 0 = ProjectAdmin
- 1 = Developer
- 2 = AppUser
- 3 = Viewer

### OrganizationRole (OrganizationMembers.Role)
- 0 = OrgAdmin
- 1 = Developer
- 2 = AppUser

## Find Your User ID

```sql
-- Find your user ID by email
SELECT Id, UserName, Email 
FROM AspNetUsers 
WHERE Email = 'your-email@example.com';
```

## Check Your Current Roles

```sql
-- Check all your organization roles
SELECT 
    o.Name AS Organization,
    om.Role AS YourRole,
    CASE om.Role 
        WHEN 0 THEN 'OrgAdmin'
        WHEN 1 THEN 'Developer'
        WHEN 2 THEN 'AppUser'
    END AS RoleName,
    om.JoinedAt
FROM OrganizationMembers om
INNER JOIN Organizations o ON om.OrganizationId = o.Id
WHERE om.UserId = 'YOUR-USER-ID' AND om.IsActive = 1;

-- Check all your project roles
SELECT 
    p.Name AS Project,
    pm.Role AS YourRole,
    CASE pm.Role 
        WHEN 0 THEN 'ProjectAdmin'
        WHEN 1 THEN 'Developer'
        WHEN 2 THEN 'AppUser'
        WHEN 3 THEN 'Viewer'
    END AS RoleName,
    pm.JoinedAt
FROM ProjectMembers pm
INNER JOIN Projects p ON pm.ProjectId = p.Id
WHERE pm.UserId = 'YOUR-USER-ID' AND pm.IsActive = 1;
```

## Common Mistakes

### ❌ Looking in AspNetRoles
```sql
-- This is WRONG - you won't find ProjectAdmin or OrgAdmin here
SELECT * FROM AspNetRoles;
-- Only shows: SYSADMIN, ADMIN, USER
```

### ✅ Looking in Membership Tables
```sql
-- This is CORRECT - check the membership tables
SELECT * FROM ProjectMembers WHERE UserId = 'YOUR-USER-ID';
SELECT * FROM OrganizationMembers WHERE UserId = 'YOUR-USER-ID';
```

## Testing Different Roles

1. **Create a test user account**
2. **Create an organization with your main account**
3. **Invite the test user with different roles:**
   - Try OrgAdmin role
   - Try Developer role
   - Try AppUser role
4. **Login as the test user and observe UI differences**

## Need More Details?

See the comprehensive documentation:
- [Roles and Permissions](./features/roles-and-permissions.md) - Full explanation
- [Organizations Feature](./features/organizations.md) - Organization-specific details

## Summary

| To Become | Method | Table | Column |
|-----------|--------|-------|--------|
| ProjectAdmin | Create a project | ProjectMembers | Role = 0 |
| OrgAdmin | Create an organization | OrganizationMembers | Role = 0 |
| Any role | Get invited by an admin | Respective membership table | Role = selected value |

**Remember:** These are NOT in AspNetRoles! They're in ProjectMembers and OrganizationMembers tables.
