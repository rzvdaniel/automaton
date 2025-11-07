# Automaton API - Quick Reference

## Available Methods

```javascript
// Execute a flow
automatonApi.executeFlow(flowName, options)

// Get app slug
automatonApi.getAppSlug()

// Custom API call (advanced)
automatonApi.customCall(path, method, body)
```

---

## Quick Examples

### Basic Flow Execution

```javascript
const result = await automatonApi.executeFlow('MyFlow');
console.log(result.OutputVariables);
```

### With Input Variables

```javascript
const result = await automatonApi.executeFlow('ProcessData', {
    inputs: {
        username: 'john',
        count: '10'
    }
});
```

### With Error Handling

```javascript
try {
    const result = await automatonApi.executeFlow('MyFlow');
    if (result.Success) {
        console.log('? Success:', result.OutputVariables);
    }
} catch (error) {
    console.error('? Error:', error.message);
}
```

### With Specific Runner

```javascript
const result = await automatonApi.executeFlow('MyFlow', {
    runnerNames: ['Production-Runner']
});
```

---

## Response Format

```javascript
{
  Success: true,
  FlowId: "guid",
  Status: "Success",
  OutputVariables: {
    key1: "value1",
    key2: "value2"
  },
  Logs: ["log1", "log2"]
}
```

---

## Common Patterns

### Form Submission

```html
<form onsubmit="handleSubmit(event)">
    <input type="text" id="name" required>
    <button type="submit">Submit</button>
</form>

<script>
async function handleSubmit(event) {
    event.preventDefault();
    const name = document.getElementById('name').value;
    
    const result = await automatonApi.executeFlow('ProcessForm', {
        inputs: { name }
    });
    
    alert('Success: ' + result.OutputVariables.message);
}
</script>
```

### Auto-Refresh Data

```javascript
async function loadData() {
    const result = await automatonApi.executeFlow('GetData');
    document.getElementById('data').textContent = result.OutputVariables.data;
}

// Load initially
loadData();

// Refresh every 30 seconds
setInterval(loadData, 30000);
```

### Display Results in Table

```javascript
const result = await automatonApi.executeFlow('GetUsers');
const users = JSON.parse(result.OutputVariables.users);

const table = users.map(user => `
    <tr>
        <td>${user.name}</td>
        <td>${user.email}</td>
    </tr>
`).join('');

document.getElementById('tableBody').innerHTML = table;
```

---

## Error Handling

```javascript
try {
    const result = await automatonApi.executeFlow('MyFlow');
} catch (error) {
    if (error.message.includes('timeout')) {
        console.error('Flow took too long - check runner status');
    } else if (error.message.includes('not found')) {
        console.error('Flow does not exist');
    } else {
        console.error('Unexpected error:', error.message);
    }
}
```

---

## Security Notes

? **DO:**
- Use `automatonApi.executeFlow()` for all flow executions
- Validate user input before sending
- Handle errors gracefully

? **DON'T:**
- Don't use `fetch()` to call `/api/web-apps/*` (will be blocked)
- Don't expose sensitive data in HTML
- Don't assume flows will always succeed

---

## Debugging

Open browser DevTools (F12):

```javascript
// Check if API is available
console.log(automatonApi);

// Check current app slug
console.log(automatonApi.getAppSlug());

// Test flow execution with detailed logging
automatonApi.executeFlow('MyFlow')
    .then(r => console.log('? Success:', r))
    .catch(e => console.error('? Error:', e));
```

---

**Full Documentation:** [Web Apps Guide](user-guide/web-apps.md)
