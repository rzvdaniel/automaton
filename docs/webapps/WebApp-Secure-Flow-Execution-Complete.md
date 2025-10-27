# WebApp Flow Execution - Secure Authentication Implementation

## ? Complete Implementation

Implemented secure flow execution for published web apps using **app-owner authentication** with WebApp-scoped API keys.

---

## ?? Security Architecture

```
???????????????????????????????????????????????????????????????
? 1. App Owner publishes web app       ?
?    - App is linked to owner's user account          ?
?    - Owner must have a WebApp-scoped API key   ?
???????????????????????????????????????????????????????????????
       ?
               ?
???????????????????????????????????????????????????????????????
? 2. Anonymous/Authenticated user accesses /web/my-app        ?
???????????????????????????????????????????????????????????????
    ?
              ?
???????????????????????????????????????????????????????????????
? 3. User calls window.Automaton.executeFlow(flowId)          ?
?    - JavaScript includes X-WebApp-Slug header    ?
???????????????????????????????????????????????????????????????
?
                 ?
???????????????????????????????????????????????????????????????
? 4. Server (FlowExecutionController):          ?
?    a) Extract app slug from X-WebApp-Slug header        ?
?    b) Find web app in database by slug             ?
?    c) Get app owner's user ID    ?
?    d) Retrieve owner's active WebApp API key      ?
? e) Validate API key exists and is not expired    ?
?    f) Execute flow using owner's permissions/runners     ?
?    g) Log: anonymous/user ? app ? owner ? execution         ?
???????????????????????????????????????????????????????????????
```

---

## ?? Key Features

### 1. **New API Key Scope: WebApp**

```csharp
public enum ApiKeyScope
{
    ReadOnly = 1,      // Read flows and executions only
 Execute = 2,       // Execute workflows
    Runner = 3,   // Register and operate runners
    WebApp = 4,        // Execute flows on behalf of web apps ? NEW
    FullAccess = 5     // All permissions
}
```

**Purpose:**
- Isolates web app flow execution from other operations
- Prevents web apps from registering runners or modifying flows
- Enables separate rate limiting and auditing
- API key prefix: `ak_webapp_...`

### 2. **Server-Side Authentication**

**FlowExecutionController** now:
- ? Extracts app slug from `X-WebApp-Slug` header (sent by JavaScript)
- ? Finds the web app in database
- ? Gets the app owner's user ID
- ? Retrieves owner's active WebApp API key
- ? Executes flow using owner's permissions
- ? **NO API key exposed to client/browser**

### 3. **Client-Side Integration**

JavaScript bridge automatically sends app slug:

```javascript
window.Automaton = {
    executeFlow: async function(flowId, inputs = {}, runnerNames = null) {
        const response = await fetch('/api/flows/execute', {
            method: 'POST',
     headers: {
         'Content-Type': 'application/json',
       'X-WebApp-Slug': 'my-app'  // ? Automatic
  },
    body: JSON.stringify({ flowId, inputs, runnerNames })
        });
        return await response.json();
    }
};
```

---

## ?? Setup Guide

### Step 1: Create WebApp API Key

**As App Owner:**

1. Navigate to **Settings ? API Keys**
2. Click **"Create API Key"**
3. Fill in details:
   - **Name:** "My WebApps Flows"
   - **Scope:** Select **"WebApp"**
   - **Description:** "For executing flows from published web apps"
   - **Expires At:** (optional)
4. Click **Create**
5. ?? **Copy the API key** - it's only shown once!

**API Key Format:** `ak_webapp_abc123...`

### Step 2: Publish Web App

1. Create/design your web app in Automaton Studio
2. Publish the app with a slug (e.g., "customer-portal")
3. The app is automatically linked to your user account
4. Web app can now execute flows using your WebApp API key

### Step 3: Use in Web App

```html
<!DOCTYPE html>
<html>
<head>
    <title>Customer Portal</title>
</head>
<body>
    <h1>Welcome</h1>
    <button onclick="processOrder()">Process Order</button>
    
    <script>
        async function processOrder() {
try {
    // No API key needed - handled server-side!
       const result = await window.Automaton.executeFlow(
          'order-processing-flow-id',
  { orderId: '12345', customer: 'John' },
       ['Production-Runner']  // Optional
    );
  
             alert('Order processed: ' + result.status);
        } catch (error) {
     alert('Error: ' + error.message);
      }
 }
    </script>
</body>
</html>
```

---

## ?? Security Benefits

### 1. **No API Keys in Browser**
- ? API keys never exposed to client
- ? Users can't steal or misuse API keys
- ? No risk from browser dev tools or network inspection

### 2. **App Owner Accountability**
- ? All flow executions tracked to app owner
- ? Owner's permissions and runners used
- ? Owner responsible for web app behavior

### 3. **Scope Isolation**
- ? WebApp API keys can ONLY execute flows
- ? Cannot register runners, modify flows, or access admin features
- ? Separate permissions for web app vs. runner operations

### 4. **Audit Trail**
- ? Logs show: anonymous user ? app ? owner ? flow execution
- ? Track which web app triggered each execution
- ? Identify suspicious patterns or abuse

---

## ?? Error Responses

### No WebApp API Key

**User hasn't created WebApp API key:**

```json
{
  "message": "App owner must create a WebApp API key to enable flow execution from web apps",
  "hint": "Go to Settings ? API Keys and create an API key with 'WebApp' scope"
}
```

**Status:** `401 Unauthorized`

### Web App Not Found

```json
{
  "message": "Web app not found or not published"
}
```

**Status:** `404 Not Found`

### No Online Runners

```json
{
  "success": false,
  "message": "No online runners available to execute the flow"
}
```

**Status:** `400 Bad Request`

### Invalid Request Context

```json
{
  "message": "This endpoint can only be called from published web apps"
}
```

**Status:** `401 Unauthorized`

---

## ?? Testing

### Test Scenario 1: Successful Execution

**Prerequisites:**
- App owner has active WebApp API key
- Web app is published
- At least one runner is online

**Steps:**
1. Access `/web/my-app`
2. Call `window.Automaton.executeFlow('flow-id')`
3. Check server logs

**Expected Result:**
```
[FlowExecutionController] Flow execution request from web app. Slug: my-app, FlowId: {guid}
[FlowExecutionController] Using app owner's WebApp API key. Owner: {userId}, App: my-app
[FlowExecutionController] Executing flow {guid} on 1 runner(s) on behalf of user {userId}
[FlowExecutionController] Flow executed. Success: True, Status: Success, WebApp: my-app
```

**Response:**
```json
{
  "success": true,
  "flowId": "guid",
  "status": "Success",
  "runnerCount": 1,
  "executedAt": "2024-01-15T10:30:00Z",
  "message": "Flow executed successfully"
}
```

### Test Scenario 2: Missing WebApp API Key

**Prerequisites:**
- App owner has NO WebApp API key
- Web app is published

**Steps:**
1. Access `/web/my-app`
2. Call `window.Automaton.executeFlow('flow-id')`

**Expected Result:**
```
[FlowExecutionController] No WebApp API key found for user {userId} (app: my-app)
```

**Response:** `401 Unauthorized` with hint to create WebApp API key

### Test Scenario 3: Direct API Call (No Web App Context)

**Steps:**
1. Call `/api/flows/execute` directly from Postman without `X-WebApp-Slug` header

**Expected Result:**
```
[FlowExecutionController] Flow execution attempt without web app context
```

**Response:** `401 Unauthorized` - "This endpoint can only be called from published web apps"

---

## ?? Logging & Monitoring

### Log Levels

**INFO:** Normal operations
```
Flow execution request from web app. Slug: my-app, FlowId: {guid}
Using app owner's WebApp API key. Owner: {userId}, App: my-app
Flow executed. Success: True, WebApp: my-app
```

**WARNING:** Security/configuration issues
```
Flow execution attempt without web app context
No WebApp API key found for user {userId} (app: my-app)
None of the specified runners are online
```

**ERROR:** System failures
```
Web app {slug} has no associated user
Error executing flow from web app. FlowId: {guid}
```

### Audit Trail

Each execution creates an audit record with:
- Anonymous/authenticated user (if logged in to Studio)
- Web app slug
- App owner user ID
- Flow ID
- Runner names
- Execution result
- Timestamp

---

## ?? Migration Path

### For Existing Apps

**Before (Old FlowExecutionController):**
- ? Anyone could execute any flow
- ? No authentication
- ? No accountability

**After (New WebApp Authentication):**
- ? Only app owners can enable flow execution
- ? Owner must create WebApp API key
- ? All executions tracked to owner

**Migration Steps:**
1. Existing published apps continue to work
2. App owners receive notification: "Create WebApp API key to enable flow execution"
3. After creating key, existing apps automatically use it
4. No changes needed to app HTML/JavaScript

---

## ?? UI Updates

### API Keys Page

**New Scope Option:**
- ReadOnly (orange)
- Execute (green)
- Runner (blue)
- **WebApp (cyan)** ? NEW
- FullAccess (purple)

**Description for WebApp Scope:**
> "Allows executing flows on behalf of your published web apps. One WebApp API key can be used for all your published apps."

---

## ?? Files Modified

1. **`src/Automaton.Studio.Server/Entities/ApiKey.cs`**
   - Added `WebApp = 4` to `ApiKeyScope` enum
   - Changed `FullAccess` to value `5`

2. **`src/Automaton.Studio/Models/ApiKeyModels.cs`**
   - Added `WebApp = 4` to client-side `ApiKeyScope` enum

3. **`src/Automaton.Studio.Server/Services/ApiKeyService.cs`**
   - Added `ak_webapp_` prefix case
   - Added `GetUserWebAppApiKeyAsync()` method

4. **`src/Automaton.Studio.Server/Controllers/FlowExecutionController.cs`**
   - Complete rewrite with WebApp authentication
   - Added `GetWebAppSlugFromRequest()` helper
   - Removed direct API key requirement

5. **`src/Automaton.Studio/Pages/WebAppViewer.razor`**
   - Updated JavaScript bridge to send `X-WebApp-Slug` header
   - Added app slug logging

6. **`src/Automaton.Studio/Pages/ApiKeys/ApiKeysViewModel.cs`**
   - Added WebApp scope color (cyan) and CSS class

---

## ? Benefits Summary

| Feature | Before | After |
|---------|--------|-------|
| **API Key Exposure** | In client code ? | Server-side only ? |
| **Authentication** | None ? | App owner's WebApp key ? |
| **Accountability** | Anonymous ? | Tracked to owner ? |
| **Permissions** | No restrictions ? | Scoped to WebApp ? |
| **Audit Trail** | None ? | Complete logging ? |
| **Security** | Vulnerable ? | Secure ? |

---

## ?? Future Enhancements

### Priority 1: Per-App API Keys
- Option to create separate API key for each web app
- More granular control
- Easier to revoke individual apps

### Priority 2: Flow Permissions
- WebApp API keys can whitelist specific flows
- Prevent unauthorized flow execution
- More fine-grained security

### Priority 3: Rate Limiting
- Limit executions per WebApp API key
- Per-app execution quotas
- Prevent abuse

### Priority 4: Usage Analytics
- Dashboard showing web app execution stats
- Most-used flows from web apps
- Cost tracking per web app

---

**Status:** ? **COMPLETE**  
**Date:** January 2024  
**Feature:** Secure WebApp Flow Execution with App-Owner Authentication
