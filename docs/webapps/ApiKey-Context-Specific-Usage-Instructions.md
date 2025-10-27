# Context-Specific API Key Usage Instructions

## ? Implementation Complete

Updated the API Key creation success modal to show context-specific usage instructions based on the scope of the newly created API key.

---

## ?? Changes Made

### 1. **ApiKeyScope Moved to Automaton.Core.Enums**

**File Created:** `src/Automaton.Core/Enums/ApiKeyScope.cs`

```csharp
public enum ApiKeyScope
{
    ReadOnly = 1,
    Execute = 2,
    Runner = 3,
    FullAccess = 4,
    WebApp = 5  // ? Added at the end to preserve existing data
}
```

**Why this change?**
- ? Shared enum used by both client and server
- ? `WebApp = 5` added at the END to avoid breaking existing serialized data
- ? Single source of truth for API key scopes

### 2. **Updated ApiKeysViewModel**

**File:** `src/Automaton.Studio/Pages/ApiKeys/ApiKeysViewModel.cs`

**Added Properties:**
```csharp
public ApiKeyScope NewApiKeyScope { get; set; } = ApiKeyScope.FullAccess;
```

**Added Methods:**
```csharp
public string GetUsageInstructions()
{
    return NewApiKeyScope switch
    {
   Runner or Execute or FullAccess =>
"Use this API key to authenticate console runners and execute workflows:",
        WebApp =>
            "This API key enables your published web apps to execute flows securely. The key is used automatically by the server when users interact with your web apps - you don't need to configure it in your web app code.",
        ReadOnly =>
            "This API key provides read-only access to flows and execution history. Use it for monitoring, reporting, or integration with external dashboards.",
        _ => "API key created successfully."
 };
}

public bool ShowRunnerUsageExamples()
{
    return NewApiKeyScope == Runner || 
   NewApiKeyScope == Execute || 
     NewApiKeyScope == FullAccess;
}
```

### 3. **Updated Success Modal**

**File:** `src/Automaton.Studio/Pages/ApiKeys/ApiKeysPage.razor`

**Before:**
```html
<strong>Usage Examples:</strong>
<div style="margin-top: 8px;">
    <code>automaton-runner auth --api-key {key}</code>
    <br />
    <code>export AUTOMATON_API_KEY="{key}"</code>
</div>
```

**After:**
```html
<strong>Usage:</strong>
<p>@ViewModel.GetUsageInstructions()</p>

@if (ViewModel.ShowRunnerUsageExamples())
{
    <div style="margin-top: 16px;">
    <strong>Examples:</strong>
        <div style="margin-top: 8px;">
          <code>automaton-runner auth --api-key {key}</code>
            <br />
         <code>export AUTOMATON_API_KEY="{key}"</code>
        </div>
    </div>
}
```

---

## ?? Usage Instructions by Scope

### **Runner, Execute, FullAccess** (Scopes 2, 3, 4)

**Message:**
> "Use this API key to authenticate console runners and execute workflows:"

**Examples Shown:**
```bash
automaton-runner auth --api-key ak_runner_...
export AUTOMATON_API_KEY="ak_runner_..."
```

**Use Cases:**
- Authenticate console runners
- Execute workflows from command line
- Automation scripts
- CI/CD pipelines

---

### **WebApp** (Scope 5)

**Message:**
> "This API key enables your published web apps to execute flows securely. The key is used automatically by the server when users interact with your web apps - you don't need to configure it in your web app code."

**Examples Shown:** ? None (server-side only)

**Use Cases:**
- Published web apps executing flows
- Server automatically uses this key
- No client-side configuration needed
- Secure, hidden from browser

---

### **ReadOnly** (Scope 1)

**Message:**
> "This API key provides read-only access to flows and execution history. Use it for monitoring, reporting, or integration with external dashboards."

**Examples Shown:** ? None (API-only usage)

**Use Cases:**
- Monitoring dashboards
- Reporting tools
- Analytics integrations
- External read-only access
- Status pages

---

## ?? User Experience

### Creating Runner/Execute/FullAccess Key:
```
??????????????????????????????????????????????????
? ? API Key Created           ?
? Your API key has been created successfully.    ?
? Copy it now as it won't be shown again.        ?
?    ?
? API Key: ak_runner_abc123...         [Copy]    ?
?   ?
? Usage:           ?
? Use this API key to authenticate console       ?
? runners and execute workflows:    ?
?      ?
? Examples: ?
? automaton-runner auth --api-key ak_runner_...  ?
? export AUTOMATON_API_KEY="ak_runner_..."       ?
?      [Done]      ?
??????????????????????????????????????????????????
```

### Creating WebApp Key:
```
???????????????????????????????????????????????????
? ? API Key Created         ?
? Your API key has been created successfully.?
? Copy it now as it won't be shown again.         ?
? ?
? API Key: ak_webapp_xyz789...       [Copy]     ?
?      ?
? Usage:      ?
? This API key enables your published web apps to ?
? execute flows securely. The key is used   ?
? automatically by the server when users interact ?
? with your web apps - you don't need to configure?
? it in your web app code.   ?
?       [Done]      ?
???????????????????????????????????????????????????
```

### Creating ReadOnly Key:
```
???????????????????????????????????????????????????
? ? API Key Created         ?
? Your API key has been created successfully.     ?
? Copy it now as it won't be shown again.         ?
? ?
? API Key: ak_readonly_def456...       [Copy]     ?
?     ?
? Usage:       ?
? This API key provides read-only access to flows ?
? and execution history. Use it for monitoring,   ?
? reporting, or integration with external         ?
? dashboards.  ?
?         [Done]      ?
???????????????????????????????????????????????????
```

---

## ? Benefits

1. **Clear Guidance**
   - Users immediately understand how to use their new API key
   - Scope-specific instructions reduce confusion
   - No irrelevant examples shown

2. **Better UX**
   - Runner keys show CLI examples
   - WebApp keys explain server-side usage
   - ReadOnly keys clarify limitations

3. **Security**
   - WebApp instructions emphasize server-side usage
   - Users understand keys shouldn't be in client code
   - Clear separation of use cases

4. **Reduced Support**
   - Self-explanatory instructions
   - Contextual help at creation time
   - Fewer "how do I use this?" questions

---

## ?? Files Modified

1. **Created:** `src/Automaton.Core/Enums/ApiKeyScope.cs`
   - Centralized ApiKeyScope enum
   - WebApp = 5 (added at end)

2. **Modified:** `src/Automaton.Studio.Server/Entities/ApiKey.cs`
   - Use `Automaton.Core.Enums.ApiKeyScope`
   - Removed local enum definition

3. **Modified:** `src/Automaton.Studio/Models/ApiKeyModels.cs`
   - Use `Automaton.Core.Enums.ApiKeyScope`
   - Removed local enum definition

4. **Modified:** `src/Automaton.Studio/Pages/ApiKeys/ApiKeysViewModel.cs`
   - Added `NewApiKeyScope` property
   - Added `GetUsageInstructions()` method
   - Added `ShowRunnerUsageExamples()` method
   - Store scope when creating API key

5. **Modified:** `src/Automaton.Studio/Pages/ApiKeys/ApiKeysPage.razor`
   - Updated success modal with conditional content
   - Show runner examples only for appropriate scopes
   - Display scope-specific usage instructions

---

## ?? Testing

### Test Case 1: Create Runner Key
1. Create API key with "Runner" scope
2. Success modal should show:
   - ? Usage message about console runners
   - ? CLI examples: `automaton-runner auth`
   - ? Environment variable example

### Test Case 2: Create WebApp Key
1. Create API key with "WebApp" scope
2. Success modal should show:
   - ? Usage message about web apps
   - ? Explanation of server-side usage
   - ? NO CLI examples

### Test Case 3: Create ReadOnly Key
1. Create API key with "ReadOnly" scope
2. Success modal should show:
   - ? Usage message about read-only access
   - ? Explanation of monitoring/reporting use
   - ? NO CLI examples

### Test Case 4: Create FullAccess Key
1. Create API key with "FullAccess" scope
2. Success modal should show:
   - ? Usage message about console runners
   - ? CLI examples shown

### Test Case 5: Create Execute Key
1. Create API key with "Execute" scope
2. Success modal should show:
   - ? Usage message about executing workflows
   - ? CLI examples shown

---

## ?? Important: Enum Value Change

**WebApp changed from 4 ? 5** to preserve existing data compatibility.

**Migration Notes:**
- Existing API keys (Runner, Execute, FullAccess) are NOT affected
- Database stores integer values, not names
- New WebApp keys will be stored as `5`
- This follows best practice: **always add new enum values at the end**

**Future Enum Additions:**
Always add new scopes at the end of the enum to avoid breaking existing data:
```csharp
public enum ApiKeyScope
{
    ReadOnly = 1,
  Execute = 2,
    Runner = 3,
    FullAccess = 4,
    WebApp = 5,
    NewScope = 6  // ? Add here, not in the middle!
}
```

---

**Status:** ? **COMPLETE**  
**Build:** ? **Successful**  
**Tests:** ?? **Manual testing recommended**  
**Hot Reload:** ? **Available (app is running)**

**Date:** January 2024  
**Feature:** Context-Specific API Key Usage Instructions
