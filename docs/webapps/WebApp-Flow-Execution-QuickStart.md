# Quick Start: WebApp Flow Execution

## For App Owners

### 1. Create WebApp API Key (One-Time Setup)

```
Settings ? API Keys ? Create API Key
??? Name: "My WebApps"
??? Scope: WebApp
??? Click Create & Save the key!
```

### 2. Publish Your Web App

```
Apps ? Your App ? Publish
??? Enter slug: "my-app"
??? Authentication: Choose public or protected
??? Click Publish
```

### 3. Done! 
Your app can now execute flows automatically.

---

## For Developers

### Execute a Flow (JavaScript)

```javascript
// Simple execution
const result = await window.Automaton.executeFlow('flow-id');

// With inputs
const result = await window.Automaton.executeFlow(
    'flow-id',
    { customer: 'John', order: '123' }
);

// With specific runner
const result = await window.Automaton.executeFlow(
    'flow-id',
    { data: 'test' },
    ['My-Runner']
);
```

### Response Format

```javascript
{
    success: true,    // Boolean: execution succeeded
    flowId: "guid",         // Flow that was executed
    status: "Success",    // Success, Error, Working
    runnerCount: 1,         // Number of runners used
    executedAt: "2024-...", // Timestamp
    message: "Flow executed successfully"
}
```

### Error Handling

```javascript
try {
    const result = await window.Automaton.executeFlow('flow-id');
    
    if (result.success) {
        console.log('? Success!');
    } else {
        console.log('?? Failed:', result.message);
    }
} catch (error) {
    // Network error, no API key, or server error
    console.error('? Error:', error.message);
    
    // Show user-friendly message
    if (error.message.includes('WebApp API key')) {
        alert('App configuration error. Please contact support.');
    }
}
```

---

## Common Issues

### ? "App owner must create a WebApp API key"

**Problem:** App owner hasn't created WebApp-scoped API key

**Solution:**
1. Go to Settings ? API Keys
2. Click "Create API Key"
3. Select Scope: "WebApp"
4. Click Create

### ? "No online runners available"

**Problem:** No runners are connected

**Solution:**
1. Start at least one runner
2. Check runner status in Automaton Studio
3. Ensure runner is connected and online

### ? "This endpoint can only be called from published web apps"

**Problem:** Calling API directly without web app context

**Solution:**
- Must be called from `/web/{slug}` context
- Don't call `/api/flows/execute` directly from external apps
- Use the `window.Automaton.executeFlow()` bridge

---

## Security Notes

? **API keys are NEVER exposed to the browser**
- Server handles authentication automatically
- Users can't steal or misuse keys

? **Flow executions use app owner's permissions**
- Owner's runners are used
- Owner's flow access rights apply
- All executions tracked to owner

? **WebApp scope is restricted**
- Can only execute flows
- Cannot register runners
- Cannot modify flows or settings

---

## Testing Checklist

- [ ] App owner created WebApp API key
- [ ] Web app is published
- [ ] At least one runner is online
- [ ] Test flow execution from web app
- [ ] Check server logs for execution details
- [ ] Test error handling (no runners, invalid flow, etc.)
- [ ] Verify audit trail in logs

---

**Need Help?**
- Check server logs for detailed error messages
- Ensure WebApp API key scope (not Runner or FullAccess)
- Verify web app is published and slug matches
- Confirm runner is online and accessible

**Quick Links:**
- [Full Documentation](./WebApp-Secure-Flow-Execution-Complete.md)
- [API Key Management](../src/Automaton.Studio/Pages/ApiKeys/)
- [Flow Execution Examples](./Phase4-FlowExecutionAPI-Complete.md)
