# Data Format Conventions

## Overview

This document explains how data flows between Web Apps (frontend) and Custom Steps (backend) in Automaton. Understanding these conventions is essential for building reliable integrations.

**Key Principle**: All values in transport are **JSON-serialized strings**. The API uses `Dictionary<string, string>` for both inputs and outputs.

---

## Data Flow Diagram

```
???????????????????????????????????????????????????????????????????????????????
?                           FRONTEND (JavaScript)                              ?
???????????????????????????????????????????????????????????????????????????????
? SENDING INPUTS:                   ? RECEIVING OUTPUTS:                      ?
?                                   ?                                         ?
? inputs: {                         ? const outputs = result.outputVariables; ?
?   'count': String(5),       ?"5"  ?                                         ?
?   'enabled': String(true),  ?"tr" ? // Parse JSON strings:                  ?
?   'config': JSON.stringify({...}) ? success = outputs.success === 'true';   ?
? }                                 ? cpu = parseFloat(outputs.cpu_percent);  ?
?                                   ? data = JSON.parse(outputs.metrics);     ?
???????????????????????????????????????????????????????????????????????????????
                   ?                                            ?
                   ?                                            ?
???????????????????????????????????????????????????????????????????????????????
?                              API (C#)                                        ?
?                                                                              ?
?  Request:  Dictionary<string, string> Inputs                                ?
?  Response: Dictionary<string, string> OutputVariables                       ?
???????????????????????????????????????????????????????????????????????????????
                   ?                                            ?
                   ?                                            ?
???????????????????????????????????????????????????????????????????????????????
?                           BACKEND (Python)                                   ?
???????????????????????????????????????????????????????????????????????????????
? RECEIVING INPUTS (strings):       ? SENDING OUTPUTS (native types):         ?
?                                   ?                                         ?
? # Convert from strings:           ? # Output native Python types:           ?
? count = int(input_count)          ? success = True        ? "true"          ?
? enabled = input_enabled == 'true' ? cpu_percent = 45.2    ? "45.2"          ?
? config = json.loads(input_config) ? metrics = {"cpu":45.2}? '{"cpu":45.2}'  ?
?                                   ?                                         ?
?                                   ? # Runner serializes automatically!      ?
?                                   ? # NEVER use json.dumps() on outputs     ?
???????????????????????????????????????????????????????????????????????????????
```

---

## Frontend ? Backend (Inputs)

### Rule: All input values must be strings

The API expects `Dictionary<string, string>` for inputs. Convert all values to strings before sending.

### JavaScript Examples

```javascript
const result = await automatonApi.executeStep('MyStep', {
    inputs: {
        // Strings - use as-is
        'name': 'John Doe',
        'email': 'john@example.com',
        
        // Numbers - convert with String()
        'count': String(5),              // "5"
        'price': String(19.99),          // "19.99"
        'refresh_interval': String(30),  // "30"
        
        // Booleans - convert with String()
        'enabled': String(true),         // "true"
        'include_details': String(false),// "false"
        
        // Objects/Arrays - convert with JSON.stringify()
        'config': JSON.stringify({ retries: 3, timeout: 30 }),
        'items': JSON.stringify(['item1', 'item2', 'item3']),
        
        // Dates - convert to ISO string
        'start_date': new Date().toISOString()
    }
});
```

### Common Mistake

```javascript
// ? WRONG - sending raw number causes API error
inputs: { 'count': 5 }

// ? CORRECT - convert to string
inputs: { 'count': String(5) }
```

---

## Backend Input Handling (Python)

### Rule: Input variables arrive as strings - convert as needed

All input variables are injected as global string variables. Convert them to the types your code needs.

### Python Examples

```python
import json

# String inputs - use directly
name = input_name  # Already a string

# Number inputs - convert with int() or float()
count = int(input_count)           # "5" ? 5
price = float(input_price)         # "19.99" ? 19.99
threshold = float(input_threshold) # "0.75" ? 0.75

# Boolean inputs - compare string value
enabled = input_enabled.lower() == 'true'      # "true" ? True
include_details = input_include.lower() == 'true'  # "false" ? False

# JSON inputs - parse with json.loads()
config = json.loads(input_config)  # '{"retries":3}' ? {"retries": 3}
items = json.loads(input_items)    # '["a","b"]' ? ["a", "b"]

# Date inputs - parse ISO string
from datetime import datetime
start_date = datetime.fromisoformat(input_start_date.replace('Z', '+00:00'))
```

### Safe Conversion with Defaults

```python
# Safe integer conversion with default
def safe_int(value, default=0):
    try:
        return int(value) if value else default
    except (ValueError, TypeError):
        return default

count = safe_int(input_count, default=10)

# Safe JSON parsing with default
def safe_json(value, default=None):
    try:
        return json.loads(value) if value else default
    except (json.JSONDecodeError, TypeError):
        return default if default is not None else {}

config = safe_json(input_config, default={"retries": 3})
```

---

## Backend Output Handling (Python)

### Rule: Output native Python types - the runner serializes them

The runner automatically serializes output variables to JSON strings. **Never call `json.dumps()` on outputs.**

### Python Examples

```python
# ? CORRECT - Output native types

# Booleans - use Python True/False
success = True
is_valid = False

# Numbers - use native int/float
cpu_percent = 45.2
count = 10
total = 1234567

# Strings - use plain strings
error_message = ""
status = "completed"
message = "Operation successful"

# Complex data - use dict/list directly
metrics = {
    "cpu": 45.2,
    "memory": 67.8,
    "disk": 82.1
}

results = [
    {"id": 1, "name": "Item 1"},
    {"id": 2, "name": "Item 2"}
]
```

### Common Mistakes

```python
# ? WRONG - String booleans cause parsing issues
success = "True"      # Frontend gets "True" string, not boolean

# ? CORRECT
success = True        # Frontend gets "true", can check === 'true'


# ? WRONG - String numbers require extra parsing
cpu_percent = "45.2"  # Frontend needs parseFloat() AND it's a string of a string

# ? CORRECT  
cpu_percent = 45.2    # Frontend gets "45.2", parseFloat() works correctly


# ? WRONG - json.dumps() causes double-escaping
data = json.dumps({"key": "value"})  # Frontend gets '"{\"key\":\"value\"}"'

# ? CORRECT
data = {"key": "value"}  # Frontend gets '{"key":"value"}', JSON.parse() works
```

---

## Backend ? Frontend (Outputs)

### Rule: All output values are JSON-serialized strings - parse as needed

The API returns `Dictionary<string, string>`. All values are JSON strings that need parsing.

### JavaScript Examples

```javascript
const result = await automatonApi.executeStep('MyStep', { inputs: {} });

if (result.success) {
    const outputs = result.outputVariables;
    
    // Boolean outputs - compare string
    const stepSuccess = outputs.success === 'true';
    const isValid = outputs.is_valid === 'true';
    
    // Number outputs - parse with parseFloat/parseInt
    const cpu = parseFloat(outputs.cpu_percent);    // "45.2" ? 45.2
    const count = parseInt(outputs.count, 10);      // "10" ? 10
    
    // String outputs - use directly (already strings)
    const message = outputs.message;
    const status = outputs.status;
    
    // Object/Array outputs - parse JSON
    const metrics = JSON.parse(outputs.metrics);    // '{"cpu":45.2}' ? {cpu: 45.2}
    const items = JSON.parse(outputs.items);        // '["a","b"]' ? ["a", "b"]
    
    // Handle potentially missing values
    const errorMsg = outputs.error_message || '';
    const data = outputs.data ? JSON.parse(outputs.data) : {};
}
```

### Helper Functions

```javascript
// Utility functions for parsing outputs
function parseBoolean(value) {
    return value === 'true' || value === true;
}

function parseNumber(value, defaultValue = 0) {
    const num = parseFloat(value);
    return isNaN(num) ? defaultValue : num;
}

function parseJSON(value, defaultValue = null) {
    if (!value) return defaultValue;
    try {
        return typeof value === 'string' ? JSON.parse(value) : value;
    } catch {
        return defaultValue;
    }
}

// Usage
const success = parseBoolean(outputs.success);
const cpu = parseNumber(outputs.cpu_percent, 0);
const metrics = parseJSON(outputs.metrics, {});
```

---

## Complete Example

### Frontend (JavaScript)

```javascript
async function fetchSystemMetrics() {
    try {
        const result = await automatonApi.executeStep('GetSystemMetricsStep', {
            inputs: {
                'include_partitions': String(true),
                'cpu_interval': String(1)
            }
        });
        
        if (result.success) {
            const outputs = result.outputVariables;
            
            // Parse outputs
            const stepSuccess = outputs.success === 'true';
            
            if (stepSuccess) {
                const cpu = parseFloat(outputs.cpu_percent);
                const memory = parseFloat(outputs.memory_percent);
                const metrics = JSON.parse(outputs.metrics);
                
                // Use the data
                updateDashboard(cpu, memory, metrics);
            } else {
                showError(outputs.error_message);
            }
        } else {
            showError(result.error || 'API request failed');
        }
    } catch (error) {
        showError(error.message);
    }
}
```

### Backend (Python)

```python
import psutil
import json

# Initialize outputs
success = False
error_message = ""
cpu_percent = 0.0
memory_percent = 0.0
metrics = {}

try:
    # Parse inputs (arrive as strings)
    include_partitions = input_include_partitions.lower() == 'true'
    cpu_interval = float(input_cpu_interval) if input_cpu_interval else 1.0
    
    # Get system metrics
    cpu = psutil.cpu_percent(interval=cpu_interval)
    memory = psutil.virtual_memory()
    
    # Set outputs as native types (runner serializes)
    cpu_percent = cpu
    memory_percent = memory.percent
    
    # Complex data as dict (NOT json.dumps!)
    metrics = {
        "cpu": cpu,
        "memory": memory.percent,
        "memory_total_gb": round(memory.total / (1024**3), 2),
        "memory_used_gb": round(memory.used / (1024**3), 2)
    }
    
    if include_partitions:
        metrics["partitions"] = []
        for p in psutil.disk_partitions(all=False):
            try:
                usage = psutil.disk_usage(p.mountpoint)
                metrics["partitions"].append({
                    "device": p.device,
                    "mountpoint": p.mountpoint,
                    "percent": usage.percent
                })
            except:
                pass
    
    success = True
    print(f"INFO: CPU: {cpu}%, Memory: {memory.percent}%")
    
except Exception as e:
    error_message = str(e)
    print(f"ERROR: {error_message}")
```

---

## Summary Table

| Direction | Data Type | Frontend Action | Backend Action |
|-----------|-----------|-----------------|----------------|
| **Frontend ? Backend** | String | Use as-is | Use as-is |
| **Frontend ? Backend** | Number | `String(value)` | `int(val)` or `float(val)` |
| **Frontend ? Backend** | Boolean | `String(value)` | `val.lower() == 'true'` |
| **Frontend ? Backend** | Object/Array | `JSON.stringify(value)` | `json.loads(val)` |
| **Backend ? Frontend** | String | Use as-is | Use as-is |
| **Backend ? Frontend** | Number | `parseFloat(val)` | Use native `int`/`float` |
| **Backend ? Frontend** | Boolean | `val === 'true'` | Use native `True`/`False` |
| **Backend ? Frontend** | Object/Array | `JSON.parse(val)` | Use native `dict`/`list` |

---

## Key Takeaways

1. **All transport values are strings** - the API uses `Dictionary<string, string>`
2. **Frontend must stringify inputs** - use `String()` for primitives, `JSON.stringify()` for objects
3. **Backend receives strings** - convert inputs with `int()`, `float()`, `json.loads()`, etc.
4. **Backend outputs native types** - runner handles JSON serialization automatically
5. **Never use `json.dumps()` on outputs** - this causes double-escaping issues
6. **Frontend must parse outputs** - use `parseFloat()`, `=== 'true'`, `JSON.parse()` as needed

---

## Related Documentation

- [API Reference](./07-api-reference.md) - Complete API documentation
- [Frontend Design Standards](./10-frontend-design-standards.md) - UI/UX guidelines
- [Phase 1: Direct Step Execution](./01-phase1-direct-step-execution.md) - Implementation details
