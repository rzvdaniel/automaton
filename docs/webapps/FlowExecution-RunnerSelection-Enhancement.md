# Flow Execution API - Runner Selection Enhancement

## ? Improvement Summary

Enhanced the Flow Execution API with intelligent runner selection to give web apps more control over where flows execute.

---

## ?? Key Changes

### 1. Optional Runner Names Parameter
**Before:**
- Flow executed on **all** available online runners
- No control over which runners to use
- Could waste resources

**After:**
- Optional `runnerNames` array in request
- Execute on specific named runners
- Auto-select first available if not specified

### 2. Smart Runner Selection Logic

```
???????????????????????????????????????????
? Flow Execution Request           ?
???????????????????????????????????????????
         ?
      ?
   ????????????????????????
   ? runnerNames provided??
   ????????????????????????
    ?         ?
     Yes       No
      ?     ?
 ?     ?
   Find matching   Use first
   online runners  online runner
      ?            ?
  ?      ?
   Any found?     Execute
      ?
 Yes/No
   ?
      ?
  Execute or Error
```

### 3. Better Error Messages

**Scenario:** User specifies offline runners

**Response:**
```json
{
  "success": false,
  "message": "None of the specified runners are online. Available runners: Local-PC, Cloud-Runner",
  "availableRunners": ["Local-PC", "Cloud-Runner"]
}
```

This helps developers:
- Know which runners are actually available
- Fix their runner names
- Implement fallback logic

---

## ?? Request Format

### Basic Request (Auto-Select)
```json
{
  "flowId": "12345678-1234-1234-1234-123456789abc"
}
```
? Uses first available online runner

### With Specific Runner
```json
{
  "flowId": "12345678-1234-1234-1234-123456789abc",
  "inputs": { "name": "John" },
  "runnerNames": ["My-Local-Runner"]
}
```
? Uses only "My-Local-Runner" (if online)

### Multiple Runners
```json
{
  "flowId": "12345678-1234-1234-1234-123456789abc",
  "runnerNames": ["Runner-A", "Runner-B", "Runner-C"]
}
```
? Executes on all three runners (if online)

---

## ?? Use Cases

### Use Case 1: Location-Specific Processing
```javascript
// Run data processing on US-based runner for compliance
await window.Automaton.executeFlow(
    'data-processing-flow',
    { data: customerData },
    ['US-Runner']
);
```

### Use Case 2: Resource-Specific Tasks
```javascript
// Run heavy computation on high-performance runner
await window.Automaton.executeFlow(
    'ml-training-flow',
    { dataset: 'large' },
    ['GPU-Runner']
);
```

### Use Case 3: Development vs Production
```javascript
const runnerName = isDevelopment ? 'Dev-Runner' : 'Production-Runner';

await window.Automaton.executeFlow(
    'deployment-flow',
    { environment: isDevelopment ? 'dev' : 'prod' },
 [runnerName]
);
```

### Use Case 4: High Availability with Fallback
```javascript
async function executeWithFallback(flowId, inputs) {
    // Try primary runner
    try {
  return await window.Automaton.executeFlow(flowId, inputs, ['Primary-Runner']);
    } catch (error) {
   // Fallback to any available runner
        return await window.Automaton.executeFlow(flowId, inputs);
    }
}
```

---

## ?? Implementation Details

### Server-Side Logic

```csharp
// 1. Get all online runners
var allRunners = await _runnerService.List(cancellationToken);
var onlineRunners = allRunners.Where(r => r.Status == RunnerStatus.Online).ToList();

// 2. Select runners based on request
if (request.RunnerNames != null && request.RunnerNames.Any())
{
    // Find matching runners (case-insensitive)
  var requestedRunners = onlineRunners
  .Where(r => request.RunnerNames.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
        .ToList();
    
    if (!requestedRunners.Any())
    {
        // Return error with available runners
 return BadRequest(new {
   message = "None specified runners are online",
     availableRunners = onlineRunners.Select(r => r.Name).ToList()
});
    }
    
    selectedRunnerIds = requestedRunners.Select(r => r.Id).ToList();
}
else
{
    // Use first available online runner
    selectedRunnerIds = new List<Guid> { onlineRunners.First().Id };
}
```

### Client-Side JavaScript

```javascript
window.Automaton = {
  executeFlow: async function(flowId, inputs = {}, runnerNames = null) {
        const body = { flowId, inputs };
        
      // Add runnerNames if provided
        if (runnerNames && Array.isArray(runnerNames) && runnerNames.length > 0) {
 body.runnerNames = runnerNames;
     }
        
        const response = await fetch('/api/flows/execute', {
 method: 'POST',
       headers: { 'Content-Type': 'application/json' },
         body: JSON.stringify(body)
        });
    
        if (!response.ok) {
        const error = await response.json();
            throw new Error(error.message);
        }
      
        return await response.json();
    }
};
```

---

## ?? Logging

### Log Output Examples

**Auto-Selection:**
```
[FlowExecutionController] Flow execution request. FlowId: {guid}, User: admin, Runners: auto
[FlowExecutionController] No runners specified, using first available runner: Local-PC
[FlowExecutionController] Executing flow {guid} on 1 runner(s)
[FlowExecutionController] Flow executed. Success: True, Status: Success
```

**Specific Runner:**
```
[FlowExecutionController] Flow execution request. FlowId: {guid}, User: admin, Runners: GPU-Runner
[FlowExecutionController] Using specified runners: GPU-Runner
[FlowExecutionController] Executing flow {guid} on 1 runner(s)
[FlowExecutionController] Flow executed. Success: True, Status: Success
```

**Offline Runners:**
```
[FlowExecutionController] Flow execution request. FlowId: {guid}, User: admin, Runners: Offline-Runner
[FlowExecutionController] None of the specified runners are online. Requested: Offline-Runner, Available: Local-PC, Cloud-Runner
```

---

## ? Benefits

1. **Resource Optimization**
   - Execute on specific runners instead of all available
   - Save compute resources
   - Reduce unnecessary executions

2. **Better Control**
   - Target specific environments (dev/prod)
   - Choose runners based on capabilities (GPU, location, etc.)
   - Implement custom runner strategies

3. **Improved UX**
   - Clear error messages with available runners
   - Helps developers debug runner issues
   - Enables smart fallback logic

4. **Flexibility**
   - Optional parameter - backward compatible
   - Works for both single and multiple runners
   - Case-insensitive runner name matching

---

## ?? Testing

### Test Case 1: Auto-Select (No Runners Specified)
```javascript
const result = await window.Automaton.executeFlow('flow-id');
// ? Should use first available online runner
```

### Test Case 2: Specific Runner (Online)
```javascript
const result = await window.Automaton.executeFlow('flow-id', {}, ['Local-Runner']);
// ? Should execute on Local-Runner
```

### Test Case 3: Specific Runner (Offline)
```javascript
try {
    await window.Automaton.executeFlow('flow-id', {}, ['Offline-Runner']);
} catch (error) {
    // ? Should return error with available runners
    console.log(error.message); // "None of the specified runners are online..."
}
```

### Test Case 4: Multiple Runners
```javascript
const result = await window.Automaton.executeFlow('flow-id', {}, ['R1', 'R2', 'R3']);
// ? Should execute on all three runners (if online)
// runnerCount should be 3
```

### Test Case 5: Case-Insensitive Match
```javascript
const result = await window.Automaton.executeFlow('flow-id', {}, ['my-runner']);
// ? Should match "My-Runner" (case-insensitive)
```

---

## ?? Files Modified

1. **`src/Automaton.Studio.Server/Controllers/FlowExecutionController.cs`**
   - Added `RunnerNames` property to `ExecuteFlowRequest`
   - Implemented smart runner selection logic
   - Enhanced error messages with available runners

2. **`src/Automaton.Studio/Pages/WebAppViewer.razor`**
   - Updated `executeFlow` signature to accept `runnerNames` parameter
   - Added conditional logic to include `runnerNames` in request body

3. **`docs/Phase4-FlowExecutionAPI-Complete.md`**
   - Updated documentation with new examples
   - Added use cases for runner selection
   - Documented new error responses

---

## ?? Status

? **Enhancement Complete!**
- Smart runner selection implemented
- Better error messages
- Comprehensive logging
- Documentation updated
- Backward compatible (optional parameter)

---

**Date:** January 2024  
**Feature:** Enhanced Runner Selection for Flow Execution API
