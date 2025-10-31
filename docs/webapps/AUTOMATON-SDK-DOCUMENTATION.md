# Automaton SDK Documentation

## Overview

The Automaton SDK is a JavaScript library that enables web applications to interact with Automaton flows. It provides a simple, intuitive API for executing flows, retrieving results, and handling errors.

## Installation

The SDK is automatically injected into published web apps by the Automaton platform. No manual installation is required.

### Automatic Injection

When you publish a web app through Automaton Studio, the SDK is automatically loaded and initialized:

```html
<script src="/js/automaton-sdk.js"></script>
<script>
    window.Automaton.appId = 'your-app-id'; // Set automatically
</script>
```

## API Reference

### `window.Automaton`

The global object that provides access to all SDK functionality.

#### Properties

##### `appId`
- **Type:** `string | null`
- **Description:** The unique identifier of the current web app. Set automatically by the Automaton platform.
- **Default:** `null`

```javascript
console.log(window.Automaton.appId); // '123e4567-e89b-12d3-a456-426614174000'
```

---

### Methods

#### `executeFlow(flowId, inputs, runnerNames)`

Executes a flow with the specified parameters.

**Signature:**
```javascript
executeFlow(flowId, inputs = {}, runnerNames = null): Promise<FlowExecutionResult>
```

**Parameters:**

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `flowId` | `string` | Yes | - | The GUID of the flow to execute |
| `inputs` | `object` | No | `{}` | Input variables to pass to the flow |
| `runnerNames` | `string[]` | No | `null` | Array of runner names to execute the flow on |

**Returns:** `Promise<FlowExecutionResult>`

**Throws:** 
- `Error` if SDK is not initialized (appId is missing)
- `Error` if flowId is not provided
- `Error` if HTTP request fails

**Example:**

```javascript
// Basic execution
const result = await window.Automaton.executeFlow('flow-guid-here');

// With input variables
const result = await window.Automaton.executeFlow('flow-guid-here', {
customer: 'John Doe',
    orderNumber: '12345',
    amount: 99.99
});

// With specific runners
const result = await window.Automaton.executeFlow('flow-guid-here', 
    { data: 'test' },
    ['Runner1', 'Runner2']
);

// With error handling
try {
    const result = await window.Automaton.executeFlow('flow-guid-here', {
    input1: 'value1'
    });
    
    if (result.success) {
        console.log('Flow executed successfully!');
        console.log('Output:', result.outputVariables);
    } else {
        console.error('Flow execution failed:', result.message);
    }
} catch (error) {
    console.error('Error executing flow:', error.message);
}
```

---

#### `getOutputVariable(result, variableName)`

Retrieves a specific output variable from a flow execution result.

**Signature:**
```javascript
getOutputVariable(result, variableName): any
```

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `result` | `FlowExecutionResult` | Yes | The flow execution result object |
| `variableName` | `string` | Yes | The name of the output variable to retrieve |

**Returns:** The value of the output variable, or `undefined` if not found

**Example:**

```javascript
const result = await window.Automaton.executeFlow('flow-guid-here');

// Get a specific output variable
const orderId = window.Automaton.getOutputVariable(result, 'orderId');
const totalAmount = window.Automaton.getOutputVariable(result, 'totalAmount');

console.log('Order ID:', orderId);
console.log('Total Amount:', totalAmount);

// Handle missing variables
const missingVar = window.Automaton.getOutputVariable(result, 'nonexistent');
if (missingVar === undefined) {
    console.log('Variable not found');
}
```

---

#### `isSuccess(result)`

Checks if a flow execution was successful.

**Signature:**
```javascript
isSuccess(result): boolean
```

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `result` | `FlowExecutionResult` | Yes | The flow execution result object |

**Returns:** `boolean` - `true` if the execution was successful, `false` otherwise

**Example:**

```javascript
const result = await window.Automaton.executeFlow('flow-guid-here');

if (window.Automaton.isSuccess(result)) {
    console.log('Success! Processing results...');
    // Process output variables
} else {
  console.error('Execution failed:', result.message);
    // Handle failure
}
```

---

#### `getLogs(result)`

Retrieves the execution logs from a flow execution result.

**Signature:**
```javascript
getLogs(result): string[]
```

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `result` | `FlowExecutionResult` | Yes | The flow execution result object |

**Returns:** `string[]` - Array of log messages, or empty array if no logs available

**Example:**

```javascript
const result = await window.Automaton.executeFlow('flow-guid-here');

const logs = window.Automaton.getLogs(result);

// Display logs
logs.forEach(log => {
    console.log(log);
});

// Or join them
console.log(logs.join('\n'));
```

---

## Type Definitions

### `FlowExecutionResult`

The result object returned by `executeFlow()`.

```typescript
interface FlowExecutionResult {
    success: boolean;     // Whether the execution was successful
    flowId: string;     // GUID of the executed flow
    status: string;    // Execution status: "Success", "Error", "Working", etc.
    runnerCount: number;  // Number of runners that executed the flow
    executedAt: string;  // ISO 8601 timestamp of execution
    message: string;// Human-readable message about the execution
    outputVariables: Record<string, any>; // Output variables from the flow
    logs?: string[];        // Optional array of log messages
}
```

**Example Result:**
```json
{
 "success": true,
    "flowId": "123e4567-e89b-12d3-a456-426614174000",
    "status": "Success",
 "runnerCount": 1,
    "executedAt": "2024-01-15T10:30:00Z",
    "message": "Flow executed successfully",
    "outputVariables": {
 "orderId": "ORD-12345",
   "totalAmount": 99.99,
        "customer": {
       "name": "John Doe",
         "email": "john@example.com"
        }
    },
    "logs": [
        "[10:30:00] [Information] Start workflow: Order Processing",
        "[10:30:01] [Information] Executing step: Validate Order",
        "[10:30:02] [Information] Completed step: Validate Order",
      "[10:30:03] [Information] End workflow: Order Processing"
    ]
}
```

---

## Usage Examples

### Basic Flow Execution

```javascript
// Execute a simple flow
async function processOrder() {
    try {
   const result = await window.Automaton.executeFlow('order-flow-guid');
        
        if (window.Automaton.isSuccess(result)) {
   alert('Order processed successfully!');
        } else {
    alert('Order processing failed: ' + result.message);
        }
    } catch (error) {
        alert('Error: ' + error.message);
    }
}
```

### Form Submission with Flow Execution

```html
<form id="orderForm">
    <input type="text" id="customerName" placeholder="Customer Name" required>
    <input type="email" id="customerEmail" placeholder="Email" required>
    <input type="number" id="amount" placeholder="Amount" required>
    <button type="submit">Submit Order</button>
</form>

<div id="result"></div>

<script>
document.getElementById('orderForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    
    const resultDiv = document.getElementById('result');
    resultDiv.textContent = 'Processing...';
    
    try {
        const result = await window.Automaton.executeFlow('order-flow-guid', {
     customerName: document.getElementById('customerName').value,
          customerEmail: document.getElementById('customerEmail').value,
          amount: parseFloat(document.getElementById('amount').value)
     });
        
        if (window.Automaton.isSuccess(result)) {
  const orderId = window.Automaton.getOutputVariable(result, 'orderId');
            resultDiv.innerHTML = `
     <p style="color: green;">? Order created successfully!</p>
            <p>Order ID: ${orderId}</p>
         `;
        } else {
     resultDiv.innerHTML = `
         <p style="color: red;">? Failed: ${result.message}</p>
        `;
        }
    } catch (error) {
        resultDiv.innerHTML = `
            <p style="color: red;">Error: ${error.message}</p>
        `;
    }
});
</script>
```

### Displaying Flow Logs

```javascript
async function executeAndShowLogs() {
    const result = await window.Automaton.executeFlow('debug-flow-guid');
    
    // Display logs in a container
    const logsContainer = document.getElementById('logs');
    const logs = window.Automaton.getLogs(result);
    
    logsContainer.innerHTML = '<h3>Execution Logs:</h3>';
    logs.forEach(log => {
        const logEntry = document.createElement('div');
        logEntry.textContent = log;
        logsContainer.appendChild(logEntry);
    });
}
```

### Multiple Output Variables

```javascript
async function getWeatherReport() {
    const result = await window.Automaton.executeFlow('weather-flow-guid', {
  city: 'San Francisco',
      units: 'imperial'
    });
    
    if (window.Automaton.isSuccess(result)) {
        const temperature = window.Automaton.getOutputVariable(result, 'temperature');
        const humidity = window.Automaton.getOutputVariable(result, 'humidity');
  const forecast = window.Automaton.getOutputVariable(result, 'forecast');
        
   document.getElementById('weather').innerHTML = `
 <h3>Weather Report</h3>
         <p>Temperature: ${temperature}°F</p>
            <p>Humidity: ${humidity}%</p>
      <p>Forecast: ${forecast}</p>
    `;
    }
}
```

### Specifying Runners

```javascript
// Execute on specific runners for different environments
async function executeOnProduction() {
    const result = await window.Automaton.executeFlow('critical-flow-guid',
     { priority: 'high' },
        ['Production-Runner-1', 'Production-Runner-2'] // Run on production runners
    );
    
    return result;
}

async function executeOnDevelopment() {
  const result = await window.Automaton.executeFlow('test-flow-guid',
{ debugMode: true },
        ['Dev-Runner'] // Run on development runner
    );
    
    return result;
}
```

### Advanced Error Handling

```javascript
async function robustFlowExecution(flowId, inputs) {
    const maxRetries = 3;
    let lastError;
    
    for (let attempt = 1; attempt <= maxRetries; attempt++) {
        try {
            console.log(`Attempt ${attempt} of ${maxRetries}...`);
            
  const result = await window.Automaton.executeFlow(flowId, inputs);
 
            if (window.Automaton.isSuccess(result)) {
    console.log('Success!');
          return result;
      } else {
    console.warn(`Attempt ${attempt} failed: ${result.message}`);
     lastError = new Error(result.message);
        }
 } catch (error) {
  console.error(`Attempt ${attempt} error:`, error);
            lastError = error;
       
            if (attempt < maxRetries) {
         // Wait before retrying (exponential backoff)
     await new Promise(resolve => setTimeout(resolve, Math.pow(2, attempt) * 1000));
  }
        }
    }
    
    throw lastError || new Error('All retry attempts failed');
}
```

---

## Alternative: Direct HTTP Calls

While the SDK provides a convenient API, you can also make direct HTTP calls to the endpoint if you need more control.

### Using Fetch API

```javascript
async function executeFlowDirectly(appId, flowId, inputs) {
    const response = await fetch(`/api/web-apps/${appId}/flows/${flowId}/execute`, {
      method: 'POST',
        headers: {
       'Content-Type': 'application/json'
        },
        body: JSON.stringify({
      inputs: inputs,
         runnerNames: ['Runner1'] // optional
        })
    });
    
    if (!response.ok) {
        const error = await response.json();
        throw new Error(error.message || 'Request failed');
    }
    
    return await response.json();
}

// Usage
const result = await executeFlowDirectly(
    window.Automaton.appId,
    'flow-guid-here',
    { customer: 'John' }
);
```

### Using Axios

```javascript
import axios from 'axios';

async function executeFlowWithAxios(flowId, inputs) {
    try {
        const response = await axios.post(
 `/api/web-apps/${window.Automaton.appId}/flows/${flowId}/execute`,
   {
     inputs: inputs,
          runnerNames: null
            },
         {
      headers: {
              'Content-Type': 'application/json'
      },
        timeout: 30000
          }
        );
    
   return response.data;
    } catch (error) {
        if (error.response?.status === 401) {
        throw new Error('Authentication required');
      }
        throw error;
    }
}
```

---

## Error Handling

### Common Errors

#### SDK Not Initialized
```javascript
// Error: Automaton SDK not initialized - appId is missing
// Cause: SDK was not loaded properly or appId was not set
// Solution: Ensure the web app is accessed through the proper URL (/web/{slug})
```

#### Flow ID Required
```javascript
// Error: flowId is required
// Cause: No flowId parameter provided to executeFlow()
// Solution: Always provide a valid flow GUID
await window.Automaton.executeFlow('valid-guid-here');
```

#### HTTP Errors
```javascript
// 401 Unauthorized
// Cause: App requires authentication and user is not logged in
// Solution: Redirect user to login page

// 404 Not Found
// Cause: App or flow not found
// Solution: Verify app is published and flow exists

// 400 Bad Request
// Cause: No runners available or invalid request
// Solution: Check that at least one runner is online
```

### Error Handling Pattern

```javascript
async function safeFlowExecution(flowId, inputs) {
    try {
      // Check if SDK is initialized
 if (!window.Automaton.appId) {
   throw new Error('SDK not initialized. Please reload the page.');
}
        
        // Execute flow
    const result = await window.Automaton.executeFlow(flowId, inputs);
        
        // Check execution status
        if (window.Automaton.isSuccess(result)) {
            return {
         success: true,
       data: result.outputVariables
      };
        } else {
          return {
   success: false,
          error: result.message,
     logs: window.Automaton.getLogs(result)
      };
        }
    } catch (error) {
        // Network or SDK errors
 console.error('Flow execution error:', error);
        
    return {
success: false,
            error: error.message,
            technical: error.stack
 };
    }
}
```

---

## Best Practices

### 1. Always Check Success Status

```javascript
const result = await window.Automaton.executeFlow(flowId);

// ? Good
if (window.Automaton.isSuccess(result)) {
    // Handle success
}

// ? Bad - don't assume success
const orderId = result.outputVariables.orderId; // May not exist if failed
```

### 2. Handle Errors Gracefully

```javascript
// ? Good - User-friendly error messages
try {
    const result = await window.Automaton.executeFlow(flowId);
    // Process result
} catch (error) {
    if (error.message.includes('Authentication required')) {
        alert('Please log in to use this feature.');
    } else {
    alert('Something went wrong. Please try again later.');
    }
}

// ? Bad - Exposing technical errors to users
try {
  const result = await window.Automaton.executeFlow(flowId);
} catch (error) {
    alert(error.stack); // Don't show stack traces to users
}
```

### 3. Validate Inputs

```javascript
// ? Good - Validate before sending
function validateOrderInputs(inputs) {
    if (!inputs.customerName || inputs.customerName.trim() === '') {
      throw new Error('Customer name is required');
  }
    if (!inputs.amount || inputs.amount <= 0) {
        throw new Error('Amount must be greater than 0');
    }
    return true;
}

try {
    validateOrderInputs(orderData);
    const result = await window.Automaton.executeFlow(flowId, orderData);
} catch (error) {
    alert(error.message);
}
```

### 4. Provide User Feedback

```javascript
// ? Good - Show loading state
async function submitForm() {
    const button = document.getElementById('submitBtn');
    button.disabled = true;
    button.textContent = 'Processing...';
    
  try {
        const result = await window.Automaton.executeFlow(flowId, formData);
        
        if (window.Automaton.isSuccess(result)) {
   showSuccessMessage('Order submitted!');
        } else {
       showErrorMessage(result.message);
        }
    } catch (error) {
   showErrorMessage('An error occurred. Please try again.');
    } finally {
    button.disabled = false;
  button.textContent = 'Submit';
    }
}
```

### 5. Use Semantic Variable Names

```javascript
// ? Good - Clear variable names
const orderResult = await window.Automaton.executeFlow('order-flow-guid');
const orderId = window.Automaton.getOutputVariable(orderResult, 'orderId');

// ? Bad - Unclear names
const r = await window.Automaton.executeFlow('order-flow-guid');
const x = window.Automaton.getOutputVariable(r, 'orderId');
```

---

## Browser Compatibility

The Automaton SDK uses modern JavaScript features and requires:

- **Fetch API** - Supported in all modern browsers
- **Async/Await** - ES2017+ (all modern browsers)
- **Promises** - ES2015+ (all modern browsers)

### Supported Browsers

| Browser | Minimum Version |
|---------|----------------|
| Chrome | 55+ |
| Firefox | 52+ |
| Safari | 11+ |
| Edge | 15+ |

### Polyfills

If you need to support older browsers, include these polyfills:

```html
<!-- Fetch polyfill -->
<script src="https://cdn.jsdelivr.net/npm/whatwg-fetch@3.6.2/dist/fetch.umd.js"></script>

<!-- Promise polyfill -->
<script src="https://cdn.jsdelivr.net/npm/promise-polyfill@8/dist/polyfill.min.js"></script>
```

---

## Debugging

### Enable Console Logging

The SDK automatically logs important events to the console:

```javascript
// SDK logs appear in browser console:
// [Automaton SDK] Loaded successfully
// [Automaton SDK] Executing flow abc123... for app xyz789...
// [Automaton SDK] Flow execution result: {...}
```

### Check SDK Status

```javascript
// Verify SDK is loaded
if (window.Automaton) {
    console.log('SDK loaded');
    console.log('App ID:', window.Automaton.appId);
} else {
    console.error('SDK not loaded!');
}

// Check if app ID is set
if (!window.Automaton.appId) {
    console.warn('App ID not set - SDK may not work correctly');
}
```

### Inspect Network Requests

Open browser DevTools (F12) and go to the Network tab to see:
- Request URL: `/api/web-apps/{appId}/flows/{flowId}/execute`
- Request payload
- Response data
- HTTP status codes

---

## Security Considerations

### 1. Flow IDs Are Visible

Flow IDs are visible in JavaScript code. This is by design and not a security concern because:
- Flows can only be executed through the web app's context
- Only the app owner's flows can be executed
- Authentication is checked server-side

### 2. Input Validation

Always validate inputs before sending to flows:

```javascript
// Sanitize user input
function sanitizeInput(input) {
    if (typeof input === 'string') {
        return input.trim().substring(0, 1000); // Limit length
    }
    return input;
}

const result = await window.Automaton.executeFlow(flowId, {
    userInput: sanitizeInput(userInput)
});
```

### 3. Authentication

The SDK respects the app's authentication settings:
- **Public apps**: Anyone can execute flows
- **Protected apps**: User must be authenticated

```javascript
// Handle authentication errors
try {
    const result = await window.Automaton.executeFlow(flowId);
} catch (error) {
    if (error.message.includes('Authentication required')) {
   // Redirect to login
        window.location.href = '/account/login?returnUrl=' + 
     encodeURIComponent(window.location.pathname);
    }
}
```

---

## Support & Resources

### Additional Documentation
- [Web App Flow Execution Security Fix](../WEB-APP-FLOW-EXECUTION-SECURITY-FIX.md)
- [Quick Reference Guide](../QUICK-REFERENCE.md)
- [Implementation Summary](../IMPLEMENTATION-SUMMARY.md)

### Getting Help
- Check browser console for error messages
- Review server logs for execution details
- Ensure runners are online and accessible
- Verify flow IDs and input variable names

---

## Version History

### v1.0.0 (Current)
- Initial release
- Flow execution with input variables
- Output variable retrieval
- Success checking
- Log access
- Error handling

---

## License

This SDK is part of the Automaton platform and follows the same license terms.

---

**Last Updated:** January 2025  
**Status:** Production Ready  
**API Version:** 1.0
