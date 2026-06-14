# Web App Integration

Guide for calling flows from published web apps using the `window.automatonApi` helper.

## Overview

Published web apps can execute Automaton flows via the `window.automatonApi` JavaScript helper. This helper is automatically injected into published apps by the platform.

## Source And Runtime

Automaton separates editable source from public runtime.

- Studio editing, GitHub sync, version history, archive import/export, MCP app tools, and fork use `source/`.
- `automaton deploy` writes only the configured `deployDir` into `runtime/`.
- Public app serving reads `runtime/` only and uses a safe web-file allowlist.
- Fork copies source only, never runtime, and only when the app is explicitly forkable.
- For AI-generated apps, prefer Vite + vanilla TypeScript unless the user asks for React, Vue, Angular, or a very small static root app.
- Plain root HTML/CSS/JS remains valid for tiny static demos with `"deployDir": "."`.
- Vite apps should use `"build": "npm run build"` and `"deployDir": "dist"`.

## Basic Usage

```javascript
// Minimal example
const flowId = 'your-flow-id';
const payload = {
  inputs: { name: 'Alice', email: 'alice@example.com' },
  runnerNames: ['runner-1']
};

window.automatonApi.executeFlow(flowId, payload)
  .then(result => console.log('Execution result', result))
  .catch(err => console.error('Execution error', err));
```

## Request Format

### Payload Structure

```javascript
const payload = {
  // Input variables for the flow
  inputs: {
    name: 'Alice',
    email: 'alice@example.com',
    orderId: '12345',
    customerId: 'cust_999'
  },
  // Optional: specific runners to execute on
  runnerNames: ['runner-1', 'runner-2']
};
```

### Example Request Body

```json
{
  "inputs": {
    "name": "Alice",
    "email": "alice@example.com"
  },
  "runnerNames": ["runner-1", "runner-2"]
}
```

## Response Format

### Success Response

```json
{
  "success": true,
  "flowId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Completed",
  "runnerCount": 2,
  "executedAt": "2025-11-09T12:34:56Z",
  "message": "Execution finished successfully",
  "outputVariables": {
    "messageId": "msg_123",
    "summary": "Processed 10 items"
  },
  "logs": [
    "Started execution on runner-1",
    "Step: FetchData - OK",
    "Step: ProcessItems - OK",
    "Execution completed"
  ]
}
```

### Error Response

```json
{
  "success": false,
  "flowId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Failed",
  "runnerCount": 0,
  "executedAt": "2025-11-09T12:34:56Z",
  "message": "Execution failed: Step validation error",
  "errors": [
    "Step 'FetchData' failed: Connection timeout",
    "No active runners available"
  ]
}
```

## Complete Example

```javascript
// HTML form submission
document.getElementById('submitForm').addEventListener('submit', async (e) => {
  e.preventDefault();
  
  const formData = new FormData(e.target);
  const flowId = '3fa85f64-5717-4562-b3fc-2c963f66afa6';
  
  const payload = {
    inputs: {
      name: formData.get('name'),
      email: formData.get('email'),
      message: formData.get('message')
    },
    runnerNames: ['runner-1']
  };
  
  try {
    const result = await window.automatonApi.executeFlow(flowId, payload);
    
    if (result.success) {
      console.log('Flow executed successfully:', result);
      alert(`Success! ${result.message}`);
      console.log('Output variables:', result.outputVariables);
    } else {
      console.error('Flow execution failed:', result.message);
      alert(`Error: ${result.message}`);
    }
  } catch (error) {
    console.error('API call failed:', error);
    alert('Failed to execute flow. Please try again.');
  }
});
```

## Security Considerations

### API Keys

- Use `WebApp`-scoped API keys for published apps
- Never embed privileged keys (like `ak_runner_xxx`) in client-side code
- Consider using short-lived tokens for sensitive operations

### CORS Configuration

- Configure CORS for published app origins on the backend
- Restrict allowed origins to known published app URLs
- Avoid using wildcard (`*`) CORS policies in production

### Input Validation

```javascript
// Validate inputs before calling the API
function validateInputs(inputs) {
  if (!inputs.email || !inputs.email.includes('@')) {
    throw new Error('Invalid email address');
  }
  if (!inputs.name || inputs.name.length < 2) {
    throw new Error('Name must be at least 2 characters');
  }
  return true;
}

try {
  validateInputs(payload.inputs);
  const result = await window.automatonApi.executeFlow(flowId, payload);
  // Handle result...
} catch (error) {
  console.error('Validation error:', error.message);
}
```

## Publishing & Access Control

### App States

- **Draft**: Only visible to app owner and project members
- **Published**: Available via public URL (`/web/{publicUrl}`)

### Access Modes

- **Public**: No login required - anyone with the URL can access
- **Protected**: Requires Automaton login before accessing

### Publishing Flows

Ensure flows called by the app are also published:
- Unpublished flows cannot be executed by published apps
- Use clear status badges to track publish state
- Publish/unpublish flows independently of apps

## Best Practices

1. **Handle errors gracefully** - Always use try/catch and provide user-friendly messages
2. **Show loading states** - Display spinners or progress indicators during execution
3. **Log to console** - Help with debugging in production
4. **Validate inputs** - Check data before sending to the API
5. **Test with runners** - Ensure target runners are online and available
6. **Monitor output variables** - Use them to update the UI with flow results

## API Reference

For complete API documentation, see:
- `src/Automaton.Studio.Web/wwwroot/automatonApi.js` - Helper implementation
- AGENTS.md (Apps section) - Platform documentation
- Swagger UI: `https://localhost:7091/swagger/index.html`
