# Why Use the Automaton API SDK?

## The Problem with Direct API Calls

You might be tempted to use `fetch()` directly:

```javascript
// ? DON'T DO THIS - Won't work due to security
fetch('/api/web-apps/my-app/flows/MyFlow/execute', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ inputs: {}, runnerNames: null })
})
.then(r => r.json())
.then(data => console.log(data));
```

**Why this doesn't work:**
1. ?? **Security** - Direct API calls from iframes are blocked
2. ?? **Authentication** - JWT tokens aren't automatically attached
3. ?? **Context** - App slug isn't known to your code
4. ?? **Error Handling** - Complex error scenarios not handled

---

## The Solution: `automatonApi`

```javascript
// ? DO THIS - Simple, secure, works perfectly
const result = await automatonApi.executeFlow('MyFlow', {
    inputs: {}
});
console.log(result);
```

**Benefits:**
1. ? **Secure** - Works within security sandbox
2. ? **Authenticated** - JWT automatically included (if logged in)
3. ? **Context-Aware** - App slug automatically injected
4. ? **Simple** - No need to construct URLs or handle auth

---

## Feature Comparison

| Feature | Direct `fetch()` | `automatonApi` |
|---------|------------------|----------------|
| **Works in iframes** | ? Blocked by security | ? Designed for iframes |
| **Authentication** | ? Manual JWT handling | ? Automatic |
| **Error handling** | ? Manual | ? Built-in |
| **Retry logic** | ? Manual | ? Built-in (ping/pong) |
| **Type safety** | ? None | ? Structured responses |
| **Future-proof** | ? Breaks on API changes | ? SDK updated automatically |

---

## Real-World Example

### ? Without SDK (Complex, Insecure)

```javascript
// Complex, error-prone, doesn't work in web apps
async function executeFlow() {
    try {
        // 1. Need to know exact URL format
        const appSlug = window.location.pathname.split('/')[2]; // Fragile!
        const url = `/api/web-apps/${appSlug}/flows/MyFlow/execute`;
        
        // 2. Need to get JWT token somehow
        const token = localStorage.getItem('jwt'); // Not available in iframe!
        
        // 3. Construct request manually
        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${token}` // Won't work!
            },
            body: JSON.stringify({
                inputs: { name: 'test' },
                runnerNames: null
            })
        });
        
        // 4. Handle response manually
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }
        
        const data = await response.json();
        
        // 5. Extract actual data
        return data.data; // What if structure changes?
        
    } catch (error) {
        console.error('Failed:', error);
        throw error;
    }
}

// This WON'T WORK in web apps due to iframe security restrictions!
```

### ? With SDK (Simple, Secure)

```javascript
// Simple, secure, works perfectly
async function executeFlow() {
    return await automatonApi.executeFlow('MyFlow', {
        inputs: { name: 'test' }
    });
}

// That's it! 3 lines instead of 30+
```

---

## How the SDK Works Behind the Scenes

```
Your Code:
  automatonApi.executeFlow('MyFlow')
        ?
Automaton API SDK (automatonApi.js):
  1. Generates unique request ID
  2. Injects app slug (you don't provide it)
  3. Constructs request payload
  4. Uses secure postMessage to parent
        ?
Message Handler (automatonMessageHandler.js):
  1. Validates origin (security)
  2. Validates payload structure (security)
  3. Calls C# via JSInterop
        ?
C# Proxy (IframeProxyService):
  1. Validates path (/api/web-apps/* only)
  2. Attaches JWT token (if logged in)
  3. Makes authenticated HTTP request
        ?
API Controller (AppsFlowExecutionController):
  1. Checks app authentication requirement
  2. Executes flow as app OWNER
  3. Returns result
        ?
Response flows back through the chain
        ?
Your Code:
  Receives clean result object
```

**All this complexity is hidden from you!** Just call `executeFlow()` and it works. ??

---

## Why This Architecture?

### Security Layers

1. **Layer 1: Origin Validation** (JavaScript)
   - Only accept messages from same-origin iframes
   - Prevents cross-site attacks

2. **Layer 2: Source Validation** (JavaScript)
   - Only accept messages from child iframes
   - Prevents parent window impersonation

3. **Layer 3: Payload Validation** (JavaScript)
   - Ensure required fields present
   - Early rejection of malformed requests

4. **Layer 4: Path Whitelist** (C#)
   - Only `/api/web-apps/*` endpoints allowed
   - **MOST CRITICAL** security layer
   - Prevents arbitrary API access

5. **Layer 5: Authentication** (C#)
   - Check if app requires login
   - Attach JWT token automatically

6. **Layer 6: Authorization** (C#)
   - Execute as app owner, not viewer
   - Prevents privilege escalation

**Without the SDK, you'd have to implement all of this yourself!**

---

## Industry Standard

This pattern is used by **every major platform** that embeds third-party code:

- **Stripe**: Requires `Stripe.js` for payments
- **Google Maps**: Requires Maps JavaScript API
- **Auth0**: Requires `auth0.js` for authentication
- **Twilio**: Requires Twilio Client SDK
- **Firebase**: Requires Firebase SDK

**Why?** Because it's the **only secure way** to allow user code to interact with platform APIs.

---

## What You Can Still Use `fetch()` For

The SDK **doesn't restrict** you from calling **external APIs**:

```javascript
// ? ALLOWED - Call external services
const weather = await fetch('https://api.weather.com/...');
const news = await fetch('https://newsapi.org/...');

// ? ALLOWED - Use Axios for external APIs
const data = await axios.get('https://api.example.com/...');

// ? REQUIRED - Use automatonApi for Automaton flows
const result = await automatonApi.executeFlow('ProcessWeather', {
    inputs: { weather: JSON.stringify(weather) }
});
```

**Freedom:**
- ? Any UI framework (React, Vue, jQuery)
- ? Any HTTP library (fetch, axios, etc.)
- ? Any external API

**Restriction:**
- ? Direct calls to `/api/web-apps/*` (use `automatonApi` instead)

---

## Summary

| Aspect | Your Freedom | The Constraint |
|--------|--------------|----------------|
| **UI Framework** | ? Use anything | None |
| **External APIs** | ? Call freely | None |
| **Automaton Flows** | ? Must use SDK | Use `automatonApi` |

**This is not a limitation - it's a feature!** You get:
- ?? Security without thinking about it
- ?? Simple API without URL construction
- ?? Authentication without JWT handling
- ?? Future-proof without version worries

---

## Need More Control?

If you have **advanced use cases** that the SDK doesn't cover, use:

```javascript
automatonApi.customCall('/api/web-apps/my-app/custom-endpoint', 'POST', {
    // Your custom payload
});
```

But 99% of the time, `executeFlow()` is all you need! ??

---

**Next:** [Full Web Apps Documentation](web-apps.md)
