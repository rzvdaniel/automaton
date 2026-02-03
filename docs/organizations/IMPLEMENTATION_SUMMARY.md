# Organization Invitation Acceptance - Implementation Summary

## Overview
Implemented a **manual acceptance approach** for organization invitations with clear, simple flows:
- **Existing users with password**: Email contains accept/decline invitation link
- **New users or password-less users**: Email contains password setup link, then accept from dashboard
- All invitations require explicit user action - no forced auto-acceptance
- Users can see and manage all pending invitations from their dashboard
- Invitations automatically hidden once user joins the organization

## Backend Changes

### 1. Updated: `SignInUserQueryHandler`
**File**: `Automaton.Studio.Server/Application/Queries/Handlers/SignInUserQueryHandler.cs`
- Removed auto-acceptance logic on login
- Users now manually accept invitations via UI
- Simplified to just return completed task (no auto-accept logic)

### 2. Updated: `OrganizationService`
**File**: `Automaton.Studio.Server/Services/OrganizationService.cs`
- `InviteMemberAsync()`: 
  - Creates user account if doesn't exist (without password)
  - Generates password setup link for new/password-less users
  - Sends email with conditional link: password setup OR invitation accept based on user state
  - Passes `SetupPasswordUrl` to email template when user needs password
- `AcceptInvitationAsync()`: 
  - **Allows anonymous access** (`[AllowAnonymous]` on endpoint)
  - Works for any user with valid token (doesn't require authentication)
  - Accepts invitation by email address on invitation
  - Returns false if user not found, invitation expired, or already member
- `GetPendingInvitationsAsync()`: 
  - Returns only unexpired, unconsumed invitations
  - **Excludes invitations** for organizations user already joined
  - Filters by current user's email address
- `DeclineInvitationAsync()`: 
  - Marks invitations as consumed without accepting

### 3. Updated: `OrganizationsController`
**File**: `Automaton.Studio.Server/Controllers/OrganizationsController.cs`
- `POST /api/organizations/invitations/accept`: 
  - Now `[AllowAnonymous]` - works without authentication
  - Accepts token directly from request body
- `GET /api/organizations/invitations/pending`: Get pending invitations for current user
- `POST /api/organizations/invitations/{id}/decline`: Decline an invitation (requires auth)

### 4. Updated: `IOrganizationService` Interface
**File**: `Automaton.Studio.Server/Services/Interfaces/IOrganizationService.cs`
- Method signatures for pending invitations and decline operations

## Frontend Changes

### 1. New: `PendingInvitationsCard.vue`
**File**: `src/automaton-studio-vue/src/features/organizations/components/PendingInvitationsCard.vue`
- Displays pending invitations in a DataTable
- Shows organization name, role, and expiry date
- **Critical**: After accepting or declining, **reloads the list from API** (not just local removal)
- This prevents stale data and ensures consistency with backend state
- Loading states and toast notifications on success/error

### 2. Updated: `AcceptInvitationView.vue`
**File**: `src/automaton-studio-vue/src/features/organizations/views/AcceptInvitationView.vue`
- Routes: `/organizations/invitations/accept` (token-based URL from email)
- Accepts invitation token without requiring authentication
- Redirects to `/organizations` on success
- Redirects to login on error (in case token validation fails)
- Removed unused variable for TypeScript compilation

### 3. Updated: `organizationsService.ts`
**File**: `src/automaton-studio-vue/src/features/organizations/services/organizationsService.ts`
- Method signatures updated to match backend:
  - `acceptInvitation(token: string)` - takes token directly from URL
  - `getPendingInvitations()` - GET endpoint for current user
  - `declineInvitation(invitationId: string)` - decline operation

### 4. New: `services/index.ts`
**File**: `src/automaton-studio-vue/src/features/organizations/services/index.ts`
- Exports organizationsService to fix module resolution
- Allows components to use: `import { organizationsService } from '../services'`

## User Experience Flow

### New User Path
1. Admin invites new user via email
2. **New user email contains**:
   - Welcome message
   - **Password setup link** (SetupPasswordUrl)
   - Link: `/auth/setup-password?token=...`
3. User clicks setup link
4. User creates password
5. User is automatically logged in
6. User sees dashboard with pending invitation
7. User accepts invitation
8. User is now member of organization

### Existing User Path
1. Admin invites existing user via email
2. **Existing user email contains**:
   - Invitation message
   - **Invitation acceptance link** (InvitationUrl)
   - Link: `/organizations/invitations/accept?token=...`
3. User clicks acceptance link
4. If not logged in, redirects to login
5. After login, user sees dashboard with pending invitation
6. Alternatively, user can accept directly from pending list on dashboard
7. User is now member of organization

## Key Design Decisions

### Anonymous Invitation Acceptance
- Endpoint: `POST /api/organizations/invitations/accept` with `[AllowAnonymous]`
- Security: Validates by **email from invitation record + token** (not user identity)
- Users can accept before logging in
- Tokens are single-use (marked as consumed after first acceptance)

### Conditional Email Strategy
- **New users**: Don't have password yet → email contains password setup link
- **Existing users**: Already have password → email contains invitation acceptance link
- Reduces email confusion ("Why two links?")
- Better UX for both paths

### Pending Invitations Query Filtering
- Returns only non-consumed invitations for current user's email
- **Excludes organizations user is already member of**
- Prevents duplicate pending invitations if user already joined the organization

### List Refresh After Actions
- After accept/decline, component reloads entire list from API
- Prevents stale state and handles concurrent operations
- Ensures UI stays synchronized with backend

## Testing Checklist

- [ ] Anonymous invitation acceptance works from email link without login
- [ ] Pending invitations list shows only organizations user hasn't joined
- [ ] No duplicate pending invitations shown from same organization
- [ ] Accept button reloads list from API
- [ ] Decline button reloads list from API
- [ ] Accepted invitations disappear from pending list
- [ ] New user email contains password setup link, not invitation link
- [ ] Existing user email contains invitation link, not password setup link
- [ ] User can accept invitation after logging in
- [ ] User is added to organization with correct role after acceptance
- [ ] Invitation token cannot be used twice (consumed after acceptance)
- [ ] Invitation expires correctly (no acceptance after expiry)
- [ ] Both backends (frontend + server) compile without errors

## Database Schema

**No migration needed** - This implementation does not add new columns. Existing `OrganizationInvitations` schema is sufficient:
- `Id`, `OrganizationId`, `Email`, `Role`, `Token`, `CreatedAt`, `ExpiryDate`, `ConsumedAt`

**Note**: `AcceptanceMethod` enum and column were removed from earlier design. Final implementation uses single manual-only acceptance path without tracking method.
