# Organizations Feature Documentation

## Overview

The Organizations feature is an optional enterprise-grade capability that enables companies to manage app assignments for non-developer users. Organizations wrap Projects and provide role-based access control with three distinct roles: OrgAdmin, Developer, and AppUser.

## Architecture

### Hierarchy

```
User → Organizations → Organization Projects → Apps
```

- **User**: Individual user account
- **Organization**: Container for teams/companies
- **Organization Projects**: Projects that belong to an organization
- **Apps**: Applications that can be assigned to AppUser members

### Database Schema

**New Tables:**
- `Organizations` - Main organization entity
- `OrganizationMembers` - User membership with roles
- `OrganizationAppAssignments` - App-to-user assignments
- `OrganizationInvitations` - Token-based invitations

**Modified Tables:**
- `Projects` - Added nullable `OrganizationId` (backward compatible)
- `AspNetUsers` - Added organization navigation properties

## Roles

> **Note:** For a comprehensive explanation of how the role system works, including the distinction between ASP.NET Identity roles and application-level roles, see [Roles and Permissions Documentation](./roles-and-permissions.md).

**Organization roles are stored in the `OrganizationMembers` table**, not in `AspNetRoles`. When you create an organization, you automatically become OrgAdmin.

### OrgAdmin
- Full control over the organization
- Can invite/remove members
- Can assign apps to AppUsers
- Can update organization settings
- Can delete the organization (owner only)
- Can link/unlink projects to/from the organization

### Developer
- Can create and manage projects and apps
- Can invite new members (OrgAdmin and Developer roles)
- Cannot assign apps to users
- Cannot modify organization settings
- Cannot link/unlink projects

### AppUser
- Can only access apps assigned to them
- Cannot create projects or apps
- Limited portal view showing only assigned apps
- Cannot invite members
- Simplified UI with development tools hidden

## Features

### 1. Organization Management

**Create Organization:**
- Name and description
- Optional slug (auto-generated if not provided)
- Public signup settings
- Email domain restrictions

**Organization Settings:**
- General information (name, description)
- Access control (public signup, email domain)
- Branding (logo URL, primary color)
- Slug (read-only, auto-generated)

### 2. Membership Management

**Invite Members:**
- Email-based invitations
- Role selection (OrgAdmin, Developer, AppUser)
- Email domain validation
- 7-day invitation expiry
- Secure token-based acceptance

**Manage Members:**
- View all active members
- Remove members (except owner)
- See invitation history

### 3. App Assignment

**Assign Apps to AppUsers:**
- OrgAdmins can assign apps to AppUser members
- Multi-select assignment dialog
- Real-time search and filtering
- Status indicators (assigned/available)
- Individual unassignment

**AppUser Portal:**
- `/my-apps` route shows all assigned apps
- Simplified view for non-technical users
- Direct app launching

## API Endpoints

### Organization Management
```
GET    /api/organizations              - List user's organizations
GET    /api/organizations/{id}         - Get organization details
POST   /api/organizations              - Create organization
PUT    /api/organizations/{id}         - Update organization
DELETE /api/organizations/{id}         - Delete organization (soft)
```

### Membership
```
GET    /api/organizations/{id}/members                - List members
POST   /api/organizations/{id}/invitations            - Invite member
POST   /api/organizations/invitations/accept          - Accept invitation
DELETE /api/organizations/{id}/members/{userId}       - Remove member
```

### App Assignment
```
GET    /api/organizations/{id}/apps/assigned                   - Get assigned apps
POST   /api/organizations/{id}/apps/{appId}/assign             - Assign app
DELETE /api/organizations/{id}/apps/{appId}/assign/{userId}    - Unassign app
GET    /api/organizations/{id}/apps/{appId}/users              - Get assigned users
```

## Frontend Routes

```
/organizations                  - List organizations
/organizations/:id              - Organization dashboard
/organizations/:id/members      - Manage members
/organizations/:id/apps         - App assignments
/organizations/:id/settings     - Organization settings
/my-apps                        - AppUser portal (assigned apps)
```

## Usage Guide

### For OrgAdmins

**1. Create an Organization:**
   - Navigate to Organizations → Create Organization
   - Fill in name and description
   - Configure public signup and email domain (optional)

**2. Invite Members:**
   - Go to Organization → Members
   - Click "Invite Member"
   - Enter email and select role
   - Member receives invitation email with token

**3. Assign Apps:**
   - Go to Organization → App Assignments
   - Select an app
   - Click "Manage Access"
   - Select AppUser members to assign
   - Click "Assign Selected"

### For Developers

**1. Join Organization:**
   - Receive invitation email
   - Click invitation link or paste token
   - Accept invitation

**2. Create Projects:**
   - Projects can be linked to organizations
   - All apps in org projects are available for assignment

### For AppUsers

**1. Access Assigned Apps:**
   - Navigate to My Apps (in sidebar)
   - View all apps assigned to you
   - Click to open/launch apps

## Backend Implementation Details

### OrganizationService

**Key Methods:**
- `CreateOrganizationAsync()` - Auto-generates unique slug
- `InviteMemberAsync()` - Creates secure token, validates email domain
- `AcceptInvitationAsync()` - Validates token, creates membership
- `AssignAppToUserAsync()` - Validates project-org boundary
- `GetAssignedAppsAsync()` - Filters apps by user assignments

**Authorization:**
- `CanAccessOrganizationAsync()` - Checks membership
- `IsOrgAdminAsync()` - Validates admin privileges

### Security Features

**Invitation Tokens:**
- 64-character base64 tokens
- 7-day expiration
- One-time use only
- Secure random generation

**Slug Generation:**
- URL-friendly formatting
- Automatic conflict resolution
- Lowercase with hyphens
- Unique constraint

**Soft Deletes:**
- Organizations use `IsActive` flag
- Memberships use `IsActive` flag
- Preserves data integrity

## Frontend Implementation Details

### Component Structure

```
features/organizations/
├── components/
│   └── AppAssignmentDialog.vue    - App assignment UI
├── services/
│   └── organizationsService.ts    - API client
├── types/
│   └── index.ts                   - TypeScript types
├── views/
│   ├── OrganizationsListView.vue  - List/create orgs
│   ├── OrganizationDashboardView.vue - Overview
│   ├── OrganizationMembersView.vue - Member management
│   ├── OrganizationAppsView.vue   - App assignments
│   ├── OrganizationSettingsView.vue - Settings
│   └── AssignedAppsView.vue       - AppUser portal
└── routes.ts                       - Route definitions
```

### Key Components

**AppAssignmentDialog:**
- Filterable member list
- Multi-select with checkboxes
- Real-time status updates
- Assignment/unassignment

**OrganizationMembersView:**
- DataTable with member details
- Invite dialog
- Role badges
- Remove functionality

**OrganizationSettingsView:**
- Form sections (general, access, branding)
- Input validation
- Auto-save on changes

## Testing

### Backend Tests (Recommended)

**Unit Tests:**
```csharp
- OrganizationService_CreateOrganization_GeneratesUniqueSlug
- OrganizationService_InviteMember_ValidatesEmailDomain
- OrganizationService_AssignApp_ValidatesProjectBoundary
- OrganizationService_AcceptInvitation_ValidatesExpiry
```

**Integration Tests:**
```csharp
- OrganizationsController_CreateOrganization_ReturnsCreated
- OrganizationsController_InviteMember_SendsInvitation
- OrganizationsController_AssignApp_RequiresOrgAdmin
```

### Frontend Tests (Recommended)

**Component Tests:**
```typescript
- AppAssignmentDialog shows only AppUsers
- OrganizationMembersView filters by search
- OrganizationSettingsView validates required fields
```

## Migration Guide

### Existing Projects

**Backward Compatibility:**
- Existing projects have `OrganizationId = null`
- They remain "personal" projects
- No migration required
- Users can optionally link projects to organizations later

### Database Migration

**Run Migration:**
```bash
cd src/Automaton.Api
dotnet ef database update
```

**Rollback (if needed):**
```bash
dotnet ef database update <previous-migration-name>
```

## Best Practices

### For OrgAdmins

1. **Use Email Domain Restrictions** - Prevent unauthorized signups
2. **Assign Roles Carefully** - OrgAdmin has full control
3. **Regular Member Review** - Remove inactive members
4. **Descriptive Slugs** - Use meaningful organization names

### For Developers

1. **Link Projects to Organizations** - Enable app assignment
2. **Document Apps** - Help AppUsers understand functionality
3. **Test Assignments** - Verify AppUsers can access apps

### For AppUsers

1. **Check My Apps Regularly** - New apps may be assigned
2. **Report Issues** - Contact OrgAdmin for access problems

## Troubleshooting

### Common Issues

**Q: Invitation link doesn't work**
- Check if invitation has expired (7 days)
- Verify email address matches invitation
- Request new invitation from OrgAdmin

**Q: Cannot assign app to user**
- Verify user has AppUser role
- Check app belongs to organization project
- Ensure you have OrgAdmin permissions

**Q: Organization not showing in list**
- Verify you're an active member
- Check organization is active (not deleted)
- Refresh page or re-login

**Q: Cannot delete organization**
- Only owner can delete organizations
- Cannot delete if it has active projects
- Use soft delete (deactivate) instead

## Future Enhancements

### Planned Features

- [ ] Organization-level permissions on projects
- [ ] App usage analytics for OrgAdmins
- [ ] Custom app categories for AppUsers
- [ ] SSO integration for organizations
- [ ] Audit logs for member/app changes
- [ ] Bulk member import/export
- [ ] Organization templates

### API Extensions

- [ ] `GET /api/organizations/{id}/projects` - List org projects
- [ ] `GET /api/organizations/{id}/audit-log` - View changes
- [ ] `POST /api/organizations/{id}/sso-config` - Configure SSO

## Support

For issues or questions:
1. Check this documentation
2. Review GitHub issues
3. Contact development team
4. Submit bug reports with detailed steps

---

**Version:** 1.0.0  
**Last Updated:** January 26, 2026  
**Status:** Production Ready
