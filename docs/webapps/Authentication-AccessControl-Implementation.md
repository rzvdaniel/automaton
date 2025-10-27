# Authentication-Based Access Control Implementation

## ? Completed: January 2024

### Overview
Implemented **authentication-based access control** for published web apps. Apps are now **protected by default** (requiring Automaton login), but users can choose to make them publicly accessible during publishing.

---

## ?? Security-First Approach

**Default Behavior:** Protected (Requires Authentication)  
**Optional:** Public (No Authentication Required)

This ensures that web apps are secure by default, while still allowing users to share apps publicly when appropriate.

---

## ?? Implementation Details

### Database Schema

**Field:** `RequiresAuthentication` (bool) in `WebApp` entity  
**Default:** `true` (protected)  
**Purpose:** Controls whether authentication is required to access the published app

### Publishing Workflow

```
User clicks "Publish"
    ? Publish Dialog opens
    ? "Require users to be logged in" checkbox (checked by default)
    ? User can uncheck for public access
    ? User confirms
    ? App published with authentication setting
```

### Access Control Logic

```
User navigates to /web/{slug}
    ? PublicWebAppsController checks app.RequiresAuthentication
    ? If true AND user not authenticated:
    ? Return 401 Unauthorized
    ? Client shows "Authentication Required" message
        ? "Log In" button redirects to login with return URL
    ? If true AND user authenticated:
        ? Serve app content
    ? If false:
        ? Serve app content (no auth check)
```

---

## ?? Files Modified

### Server-Side

1. **`PublicWebAppsController.cs`**
   - Checks `RequiresAuthentication` flag
   - Returns 401 if auth required and user not authenticated
   - Logs access attempts with authentication status
   - Returns `RequiresAuthentication` in response

2. **`AppsService.cs` (Server)**
   - `Publish()` accepts `requiresAuthentication` parameter (default: true)
   - Sets `entity.RequiresAuthentication` on publish
   - Logs authentication requirement in publish action
   - `MapToModel()` includes `RequiresAuthentication`

3. **`AppsController.cs`**
   - `PublishAppRequest` includes `RequiresAuthentication` (default: true)
   - Passes to `appsService.Publish()`

4. **`WebApp.cs` (Entity)**
   - Already has `RequiresAuthentication` field

5. **`AppModel.cs` (Server)**
   - Added `RequiresAuthentication` property (default: true)

### Client-Side

1. **`WebAppViewer.razor`**
   - Checks response.RequiresAuthentication
   - Checks user authentication state via `AuthenticationStateProvider`
   - Shows "Authentication Required" error if needed
   - "Log In" button redirects with return URL
   - Handles 401 responses from API

2. **`PublishAppDialog.razor`**
   - Checkbox: "Require users to be logged in to Automaton" (checked by default)
   - Shows lock icon ?? when protected
- Shows unlock icon ?? when public
   - Clear messaging about access level

3. **`PublishAppModel.cs`**
   - `RequiresAuthentication` property (default: true)

4. **`AppsService.cs` (Client)**
   - `PublishAsync()` includes `requiresAuthentication` parameter (default: true)
   - Sends to server in publish request

5. **`AppsPage.razor.cs`**
   - Passes `model.RequiresAuthentication` to `PublishApp()`
   - Success message shows access type: "(Protected)" or "(Public)"

6. **`AppDesignerPage.razor.cs`**
   - Passes `model.RequiresAuthentication` to `PublishAsync()`
   - Updates local model with authentication setting

7. **`AppsViewModel.cs`**
   - `PublishApp()` includes `requiresAuthentication` parameter (default: true)
   - Updates local app model

8. **`AppModel.cs` (Client)**
   - Added `RequiresAuthentication` property (default: true)

9. **`AppListItem.cs` (Client)**
   - Added `RequiresAuthentication` property (default: true)

---

## ?? UI/UX

### Publish Dialog

**Protected Mode (Default):**
```
? Require users to be logged in to Automaton
?? Protected: Only authenticated Automaton users can access this app
```

**Public Mode:**
```
? Require users to be logged in to Automaton
?? Public: Anyone with the link can access this app (no login required)
```

### Access Denied Page

When unauthenticated user tries to access protected app:

```
?? Authentication Required

This app requires you to be logged in to access it.

[Log In] [Go Home]
```

- **Log In** button redirects to `/account/login?returnUrl=/web/{slug}`
- After login, user is redirected back to the app

### Success Messages

**Publishing Protected App:**
```
? App published! (Protected) Access it at /web/{slug}
```

**Publishing Public App:**
```
? App published! (Public) Access it at /web/{slug}
```

---

## ?? Security Features

### 1. Secure by Default
- All apps require authentication unless explicitly made public
- Default checkbox state: **checked** (protected)
- Developer must intentionally uncheck to make public

### 2. Server-Side Enforcement
- Authentication checked on **every request**
- Cannot bypass by manipulating client
- Returns proper HTTP 401 status

### 3. Logging & Monitoring
- All access attempts logged
- Authentication status included in logs
- Easy to audit who accessed what

### 4. Return URL Support
- Login redirects back to app after authentication
- Seamless user experience
- Preserves original intent

---

## ?? Example Scenarios

### Scenario 1: Internal Dashboard (Protected)

**Publishing:**
```
User: Creates "Sales Dashboard"
User: Clicks "Publish"
User: Keeps "Require login" checked ?
User: Enters slug "sales-dashboard"
User: Clicks "Publish"
Result: App published at /web/sales-dashboard (protected)
```

**Access:**
```
Employee (logged in): ? Can access
External user: ? Redirected to login
Anonymous user: ? Redirected to login
```

### Scenario 2: Public Landing Page (Public)

**Publishing:**
```
User: Creates "Product Showcase"
User: Clicks "Publish"
User: Unchecks "Require login" ?
User: Enters slug "products"
User: Clicks "Publish"
Result: App published at /web/products (public)
```

**Access:**
```
Employee (logged in): ? Can access
External user: ? Can access
Anonymous user: ? Can access
```

---

## ?? Testing

### Test Case 1: Protected App (Default Behavior)

1. Create and publish app with default settings (checkbox checked)
2. Open `/web/{slug}` in incognito browser
3. **Expected:** Redirected to login page
4. Log in with valid credentials
5. **Expected:** Redirected back to app, app loads

### Test Case 2: Public App

1. Create and publish app, uncheck "Require login"
2. Open `/web/{slug}` in incognito browser
3. **Expected:** App loads immediately, no login required

### Test Case 3: Changing Access Level

1. Publish app as protected
2. Access in incognito ? login required ?
3. Unpublish, republish as public
4. Access in incognito ? no login required ?

### Test Case 4: Authentication Status Display

1. Publish protected app
2. Check publish dialog shows ?? icon and "Protected" message
3. Uncheck checkbox
4. Check dialog shows ?? icon and "Public" message

### Test Case 5: Return URL After Login

1. Access protected app while not logged in
2. Click "Log In" button
3. Complete login
4. **Expected:** Redirected back to `/web/{slug}`
5. App loads normally

---

## ?? Database Migration

**Required:** Migration already exists from Phase 2  
**Field:** `RequiresAuthentication` bit NOT NULL DEFAULT 1

If not applied yet:
```bash
cd src/Automaton.Studio.Server
dotnet ef migrations add AddWebAppPublishing --context ApplicationDbContext
dotnet ef database update
```

---

## ?? Access Control Matrix

| User Type | Protected App | Public App |
|-----------|---------------|------------|
| Logged-in Automaton User | ? Access | ? Access |
| Anonymous User | ? Login Required | ? Access |
| Incognito Browser | ? Login Required | ? Access |
| External Share | ? Login Required | ? Access |

---

## ?? Best Practices

### When to Use Protected Apps
- Internal dashboards
- Employee tools
- Sensitive data visualization
- Admin panels
- Business intelligence reports

### When to Use Public Apps
- Marketing landing pages
- Product showcases
- Public forms
- Documentation sites
- Event registrations
- Customer-facing tools

---

## ?? Configuration

### Default Settings
```csharp
RequiresAuthentication = true  // Protected by default
```

### Changing Default (if needed)
To make apps public by default (not recommended):
```csharp
// In PublishAppModel.cs
public bool RequiresAuthentication { get; set; } = false;
```

**Warning:** This would compromise security. Not recommended.

---

## ?? Troubleshooting

### Issue: Can't access own published app

**Cause:** App is protected and you're not logged in  
**Solution:** Log in to Automaton first, then access app

### Issue: Want to share app but users can't access

**Cause:** App published as protected  
**Solution:** Unpublish, republish with "Require login" unchecked

### Issue: Public app still asking for login

**Cause:** Browser caching old authentication state  
**Solution:** Clear browser cache or try incognito

---

## ? Success Criteria Met

- ? Apps protected by default (secure by default)
- ? Optional public access (user choice)
- ? Clear UI indicators (lock/unlock icons)
- ? Server-side enforcement (cannot bypass)
- ? Proper HTTP status codes (401 for unauthorized)
- ? Return URL support (seamless login flow)
- ? Logging & monitoring (audit trail)
- ? Build successful, no errors

---

## ?? Documentation Updates

- ? This authentication implementation document
- ? Phase 2 summary updated
- ? Phase 3 summary updated

**Pending:**
- User guide: "Publishing Secure vs Public Apps"
- Security best practices guide
- Access control FAQ

---

**Status:** ? **COMPLETE**  
**Security Level:** High (Protected by Default)  
**User Choice:** Yes (Can make public if needed)  
**Date:** January 2024
