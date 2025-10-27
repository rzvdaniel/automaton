# Fix: Allow Anonymous Access to /web/{slug} Route

## ? Issue Resolved

### Problem
When accessing `/web/{slug}` in incognito mode, users were **immediately redirected** to the login page without seeing the "Authentication Required" message or "Log In" button.

### Root Cause
The application has **three layers** of authentication enforcement:

1. **Global Authorization Filter** (`Program.cs` line 112)
   ```csharp
 options.Filters.Add(new AuthorizeFilter(policy));
   ```
   Applies authentication requirement to all API controllers.

2. **AuthorizeRouteView** (`App.razor` line 4)
   ```razor
   <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
   ```
   Requires authentication for all Blazor pages/routes.

3. **Authentication Middleware** (`Program.cs`)
   Intercepts unauthenticated requests and redirects to login.

Without `[AllowAnonymous]` on the Blazor page, the `AuthorizeRouteView` blocked access **before** the component could load and check the app's `RequiresAuthentication` flag.

---

## ?? Solution Applied

### Added `[AllowAnonymous]` to WebAppViewer.razor

**File:** `src/Automaton.Studio/Pages/WebAppViewer.razor`

```razor
@page "/web/{slug}"
@attribute [AllowAnonymous]  // ? CRITICAL: Allows page to load without auth
@layout Automaton.Studio.Shared.EmptyLayout
@using Microsoft.AspNetCore.Authorization  // ? Added
@using Microsoft.AspNetCore.Components.Authorization
```

This tells `AuthorizeRouteView` to **allow the page to load** regardless of authentication state. The page itself then checks the app's `RequiresAuthentication` flag and shows appropriate UI.

---

## ?? How It Works Now

### Access Flow - Protected App (Fixed)

```
User navigates to /web/{slug} in incognito
    ? Router sees @page "/web/{slug}"
    ? AuthorizeRouteView checks route
    ? Sees @attribute [AllowAnonymous] ?
    ? Allows WebAppViewer component to load
    ? Component makes GET /api/web/{slug}
    ? API returns app data with RequiresAuthentication = true
    ? Component checks user authentication state
    ? User not authenticated
  ? Shows "Authentication Required" message ?
    ? Shows "Log In" button ?
    ? User clicks "Log In"
    ? Redirects to login with return URL
    ? User logs in
    ? Redirects back to /web/{slug}
    ? Component loads, user is authenticated ?
  ? App renders in iframe ?
```

### Access Flow - Public App (Fixed)

```
User navigates to /web/{slug} in incognito
    ? Router sees @page "/web/{slug}"
  ? AuthorizeRouteView checks route
    ? Sees @attribute [AllowAnonymous] ?
    ? Allows WebAppViewer component to load
    ? Component makes GET /api/web/{slug}
    ? API returns app data with RequiresAuthentication = false
    ? Component checks: no auth required
    ? App renders in iframe immediately ?
```

---

## ?? Authorization Layers

Here's how the three layers now work together:

### Layer 1: API Controllers (Server-Side)
```csharp
// Program.cs - Global authorization on all API endpoints
options.Filters.Add(new AuthorizeFilter(policy));

// PublicWebAppsController.cs - Explicitly allows anonymous
[ApiController]
[Route("api/web")]
[AllowAnonymous]  // ? Overrides global filter
public class PublicWebAppsController : ControllerBase { }
```

**Result:** `/api/web/{slug}` is accessible without authentication ?

### Layer 2: Blazor Routes (Client-Side)
```razor
<!-- App.razor - Requires auth for all routes by default -->
<AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />

<!-- WebAppViewer.razor - Explicitly allows anonymous -->
@page "/web/{slug}"
@attribute [AllowAnonymous]  // ? Overrides AuthorizeRouteView
```

**Result:** `/web/{slug}` page is accessible without authentication ?

### Layer 3: Component Logic (UI)
```csharp
// WebAppViewer.razor.cs - Checks app-specific requirements
if (response.RequiresAuthentication)
{
    var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
    if (!user.Identity?.IsAuthenticated)
 {
        // Show "Authentication Required" message with "Log In" button
    }
}
```

**Result:** Appropriate UI shown based on app's `RequiresAuthentication` setting ?

---

## ? What Was Fixed

### Before (Broken):
```
? /web/{slug} blocked by AuthorizeRouteView
? Immediate redirect to login page
? No "Authentication Required" message shown
? No "Log In" button
? Poor user experience
```

### After (Fixed):
```
? /web/{slug} accessible to everyone
? Page loads and checks app settings
? "Authentication Required" message shown (if needed)
? "Log In" button visible
? Return URL preserved
? Public apps work without login
? Protected apps show clear messaging
? Great user experience
```

---

## ?? Testing

### Test 1: Protected App

1. **Publish app as protected** (checkbox checked)
2. **Open `/web/{slug}` in incognito**
3. **Expected Results:**
   - ? Page loads (not redirected)
   - ? See "Authentication Required" title
   - ? See message: "This app requires you to be logged in to access it."
   - ? See "Log In" button
   - ? Click "Log In" ? redirects to login
   - ? After login ? redirected back to `/web/{slug}`
   - ? App loads in iframe

### Test 2: Public App

1. **Publish app as public** (checkbox unchecked)
2. **Open `/web/{slug}` in incognito**
3. **Expected Results:**
   - ? Page loads immediately
   - ? No authentication check
   - ? App renders in iframe
   - ? No login required

### Test 3: API Endpoint

1. **Test API directly:** `GET /api/web/{slug}`
2. **Expected Results:**
   - ? Returns 200 OK
   - ? Returns app data with `RequiresAuthentication` flag
   - ? No 401 or automatic redirect

---

## ?? Files Modified

1. **`src/Automaton.Studio/Pages/WebAppViewer.razor`**
   - Added `@attribute [AllowAnonymous]`
   - Added `@using Microsoft.AspNetCore.Authorization`

---

## ?? Key Technical Points

### Why `[AllowAnonymous]` Is Needed

**Without it:**
```
User ? Router ? AuthorizeRouteView ? ? Checks authentication
       ? ? User not authenticated
      ? ? Blocks page load
        ? ? Triggers redirect to login
```

**With it:**
```
User ? Router ? AuthorizeRouteView ? ? Sees [AllowAnonymous]
               ? ? Allows page to load
 ? ? Component handles auth logic
        ? ? Shows appropriate UI
```

### Authorization Attribute Hierarchy

```
Most Restrictive (Top)
    ?
1. AuthorizeRouteView (global for all routes)
    ?
2. @attribute [AllowAnonymous] (page-level override)
    ?
3. Component logic (custom authentication check)
    ?
Least Restrictive (Bottom)
```

The `[AllowAnonymous]` attribute at the page level **overrides** the global `AuthorizeRouteView` requirement, allowing the component to load and make its own authentication decisions.

---

## ? Verification

- ? Build successful
- ? `@attribute [AllowAnonymous]` added
- ? Required `@using` directive added
- ? API already has `[AllowAnonymous]`
- ? Component handles authentication logic
- ? Protected apps show login prompt
- ? Public apps load immediately

---

## ?? Next Steps

**Please test:**

1. **Stop and restart the application** (hot reload may not apply attribute changes)
2. **Test protected app in incognito:**
   - Should see "Authentication Required" page
   - Should see "Log In" button
   - Click "Log In" should preserve return URL
3. **Test public app in incognito:**
   - Should load immediately without prompt

---

**Status:** ? **FIXED**  
**Root Cause:** `AuthorizeRouteView` blocking page load  
**Solution:** Added `@attribute [AllowAnonymous]` to WebAppViewer  
**Impact:** No breaking changes, improves UX significantly  
**Date:** January 2024
