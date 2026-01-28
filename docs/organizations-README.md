# Organizations Feature

## Quick Start

### For Users

1. **Create an Organization**
   - Go to Organizations → Create Organization
   - Fill in name and description
   - **You're automatically the OrgAdmin** (role stored in `OrganizationMembers` table)

2. **Invite Members**
   - Organizations → {Your Org} → Members
   - Click "Invite Member"
   - Choose role: OrgAdmin, Developer, or AppUser

3. **Assign Apps (OrgAdmins only)**
   - Organizations → {Your Org} → App Assignments
   - Click "Manage Access" on an app
   - Select AppUser members to assign

> **Important:** Organization roles (OrgAdmin, Developer, AppUser) are NOT in the `AspNetRoles` table. They're stored in the `OrganizationMembers.Role` column. See [Roles and Permissions Documentation](./features/roles-and-permissions.md) for details.

### For Developers

**Files Modified/Created:**

**Backend:**
```
src/Automaton.Studio.Server/
├── Entities/
│   ├── Organization.cs
│   ├── OrganizationMember.cs
│   ├── OrganizationAppAssignment.cs
│   └── OrganizationInvitation.cs
├── Enums/
│   └── OrganizationRole.cs
├── Controllers/
│   └── OrganizationsController.cs
├── Services/
│   ├── Interfaces/IOrganizationService.cs
│   └── OrganizationService.cs
└── Models/
    └── OrganizationModels.cs
```

**Frontend:**
```
src/automaton-studio-vue/src/features/organizations/
├── components/
│   └── AppAssignmentDialog.vue
├── services/
│   └── organizationsService.ts
├── types/
│   └── index.ts
├── views/
│   ├── OrganizationsListView.vue
│   ├── OrganizationDashboardView.vue
│   ├── OrganizationMembersView.vue
│   ├── OrganizationAppsView.vue
│   ├── OrganizationSettingsView.vue
│   └── AssignedAppsView.vue
└── routes.ts
```

## API Overview

### Organizations
- `GET /api/organizations` - List user's organizations
- `POST /api/organizations` - Create organization
- `GET /api/organizations/{id}` - Get details
- `PUT /api/organizations/{id}` - Update (OrgAdmin only)
- `DELETE /api/organizations/{id}` - Delete (owner only)

### Members
- `GET /api/organizations/{id}/members` - List members
- `POST /api/organizations/{id}/invitations` - Invite member
- `POST /api/organizations/invitations/accept` - Accept invitation
- `DELETE /api/organizations/{id}/members/{userId}` - Remove member

### App Assignments
- `GET /api/organizations/{id}/apps/assigned` - Get assigned apps
- `POST /api/organizations/{id}/apps/{appId}/assign` - Assign app
- `DELETE /api/organizations/{id}/apps/{appId}/assign/{userId}` - Unassign
- `GET /api/organizations/{id}/apps/{appId}/users` - Get assigned users

## Roles

| Feature | OrgAdmin | Developer | AppUser |
|---------|----------|-----------|---------|
| Create Projects | ✅ | ✅ | ❌ |
| Invite Members | ✅ | ✅ (limited) | ❌ |
| Assign Apps | ✅ | ❌ | ❌ |
| Update Settings | ✅ | ❌ | ❌ |
| Access Apps | ✅ | ✅ | ✅ (assigned only) |

## Key Features

✅ **Role-Based Access Control** - 3 distinct roles  
✅ **App Assignment** - Assign apps to specific users  
✅ **Invitation System** - Secure token-based invitations  
✅ **Backward Compatible** - Existing projects unaffected  
✅ **Soft Deletes** - Organizations can be deactivated  
✅ **Slug Generation** - Auto-generated URL-friendly identifiers  

## Documentation

- [Complete Feature Documentation](../features/organizations.md)
- [Integration Testing Guide](../testing/organizations-integration-tests.md)
- [API Reference](../api/organizations-api.md) *(coming soon)*

## Support

For questions or issues:
1. Check the [documentation](../features/organizations.md)
2. Review [integration tests](../testing/organizations-integration-tests.md)
3. Submit a GitHub issue

---

**Status:** ✅ Production Ready  
**Version:** 1.0.0
