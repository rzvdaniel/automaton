# Organizations Feature - Integration Testing Guide

## Overview

This guide provides step-by-step integration tests for the Organizations feature. These tests should be performed manually or automated to ensure all functionality works end-to-end.

## Prerequisites

- Backend server running (`dotnet run` in Automaton.Studio.Server)
- Frontend dev server running (`npm run dev` in automaton-studio-vue)
- Database migration applied (`dotnet ef database update`)
- At least 2 test user accounts created

## Test Scenarios

### Scenario 1: Organization Creation

**Test:** Create a new organization

**Steps:**
1. Login as User A
2. Navigate to `/organizations`
3. Click "Create Organization"
4. Fill in:
   - Name: "Test Organization"
   - Description: "Test description"
5. Click "Create"

**Expected Results:**
- ✅ Organization created successfully
- ✅ Toast notification shows "Organization created successfully"
- ✅ User A is automatically OrgAdmin
- ✅ Organization appears in list
- ✅ Slug is auto-generated (e.g., "test-organization")

### Scenario 2: Member Invitation

**Test:** Invite a member to the organization

**Steps:**
1. As User A (OrgAdmin), navigate to organization
2. Click "Members" in dashboard or go to `/organizations/{id}/members`
3. Click "Invite Member"
4. Fill in:
   - Email: user-b@example.com
   - Role: Developer
5. Click "Send Invitation"

**Expected Results:**
- ✅ Invitation sent successfully
- ✅ Toast shows success message
- ✅ Invitation token generated
- ✅ Token expires in 7 days

### Scenario 3: App Assignment

**Test:** Assign an app to an AppUser

**Steps:**
1. As User A (OrgAdmin), go to `/organizations/{id}/apps`
2. Find an app in the list
3. Click "Manage Access"
4. AppAssignmentDialog opens
5. Search for AppUser member
6. Check the checkbox
7. Click "Assign Selected"

**Expected Results:**
- ✅ Dialog shows only AppUser members
- ✅ Search filters members correctly
- ✅ User is marked as assigned
- ✅ Toast shows "App assigned to X user(s)"

## Quick Smoke Test Checklist

- [ ] Can create organization
- [ ] Organizations appears in navigation
- [ ] Can invite member
- [ ] Can view organization dashboard
- [ ] Can update settings
- [ ] Can assign app to AppUser
- [ ] AppUser can see assigned apps
- [ ] Can remove member
- [ ] Can delete organization

---

**Version:** 1.0.0  
**Last Updated:** January 26, 2026
