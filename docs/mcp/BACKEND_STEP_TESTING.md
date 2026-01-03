# Backend Step Testing in UnifiedAppMcpToolProvider

## Overview

The `UnifiedAppMcpToolProvider` includes comprehensive backend step testing capabilities to ensure AI-generated code works correctly before being deployed to apps. This document describes the testing workflow, tools, and best practices.

**Last Updated**: January 2026

---

## Table of Contents

1. [Why Testing is Critical](#why-testing-is-critical)
2. [Auto-Linking Architecture](#auto-linking-architecture)
3. [The execute_backend_step_code Tool](#the-execute_backend_step_code-tool)
4. [Testing Workflow](#testing-workflow)
5. [Error Handling](#error-handling)
6. [Code Recommendations](#code-recommendations)
7. [Tool Reference](#tool-reference)
8. [Examples](#examples)

---

## Why Testing is Critical

Backend steps execute on Runners and interact with external systems. Testing before deployment prevents:

- **API Endpoint Errors**: 404, 500, authentication failures
- **Missing Imports**: Python module import errors
- **Type Errors**: Incorrect variable types or parsing
- **Logic Errors**: Incorrect output variable assignments
- **Broken Apps**: Apps fail at runtime without pre-deployment testing

**Impact of Untested Code:**
- Users experience runtime failures
- Debugging requires re-opening the app designer
- Loss of confidence in AI-generated code

**Solution:**
The `execute_backend_step_code` tool validates steps **before** the AI finalizes the app, enabling immediate fix-and-retest cycles.

---

## Auto-Linking Architecture

### How Steps are Created and Linked

**Before (Manual Linking)**:
```csharp
// Step 1: Create step
var step = await customStepsService.Create(newStep);

// Step 2: AI must remember to link (often forgotten!)
await appStepsService.LinkStepAsync(appId, step.Id);
```

**After (Atomic Auto-Linking)**:
```csharp
// Single atomic operation - create AND link
var linkedStep = await appStepsService.CreateStepForAppAsync(appId, stepName);
```

### Benefits

1. **Atomic Operation**: Create + link in one database transaction
2. **No Forgotten Links**: AI can't forget to link (automatic)
3. **Consistent State**: No orphaned steps
4. **Simpler Workflow**: One step instead of two

### Implementation Location

- **Method**: `AppStepsService.CreateStepForAppAsync()` (src/Automaton.Studio.Server/Services/AppStepsService.cs:38-94)
- **Tool**: `create_backend_step` in `UnifiedAppMcpToolProvider`
- **Handler**: `HandleCreateBackendStep` (src/Automaton.Studio/Services/Mcp/ToolProviders/UnifiedAppMcpToolProvider.cs:391-454)

---

## The execute_backend_step_code Tool

### Tool Definition

```json
{
  "name": "execute_backend_step_code",
  "description": "Execute and test a backend step's code with input variables. CRITICAL: Always use this to test the step after creating it and before finalizing the app.",
  "parameters": {
    "step_name": "string (required) - Name of the step to execute",
    "input_variables": "object (optional) - Test input variables as key-value pairs"
  }
}
```

### Execution Flow

```
┌─────────────────────────────────────────┐
│ 1. AI calls execute_backend_step_code  │
│    with step_name and test inputs      │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│ 2. Resolve step by name from database  │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│ 3. Load step code and language         │
│    (Python, PowerShell, Batch)          │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│ 4. Prepare input variables             │
│    - Use provided test data, or         │
│    - Use defined input vars with        │
│      empty values if none provided      │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│ 5. Execute via ICustomStepExecuteService│
│    (same service used by Runner)        │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│ 6. Build detailed result with:         │
│    - Success/failure status             │
│    - Output logs                        │
│    - Error info (line #, stack trace)   │
│    - Output variables                   │
│    - Execution time                     │
│    - Code recommendations               │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│ 7. Return JSON result to AI             │
└─────────────────────────────────────────┘
```

### Result Format

```json
{
  "success": true,
  "output": "INFO: Fetching weather for London...\nINFO: Request successful",
  "errors": [],
  "errorInfo": null,
  "outputVariables": {
    "success": "true",
    "temperature": "15.2",
    "conditions": "Cloudy"
  },
  "executionTime": "245ms",
  "inputVariablesUsed": {
    "city": "London"
  },
  "definedOutputVariables": ["success", "error_message", "temperature", "conditions"],
  "stepInfo": {
    "name": "FetchWeatherStep",
    "language": "python",
    "inputsExpected": 1,
    "outputsExpected": 4
  },
  "recommendations": {
    "items": [],
    "followsStudioPatterns": true
  }
}
```

---

## Testing Workflow

### Recommended Workflow for AI

```
1. create_backend_step
   ↓
2. suggest_step_variables
   ↓
3. save_step_code
   ↓
4. execute_backend_step_code ← CRITICAL TEST POINT
   ↓
   ├─ SUCCESS? → Proceed to save_app_code
   │
   └─ FAILURE? → Fix code, save_step_code, test again (loop)
```

### Example: Full Workflow

**Step 1: Create Backend Step**
```
Tool: create_backend_step
Input: { "name": "FetchWeatherStep", "language": "python" }
Result: "Created backend step 'FetchWeatherStep' and automatically linked to app"
```

**Step 2: Define Variables**
```
Tool: suggest_step_variables
Input: {
  "step_name": "FetchWeatherStep",
  "input_variables": [
    { "name": "city", "description": "City name for weather lookup" }
  ],
  "output_variables": [
    { "name": "success", "description": "True if successful" },
    { "name": "error_message", "description": "Error details if failed" },
    { "name": "temperature", "description": "Temperature in Celsius" },
    { "name": "conditions", "description": "Weather conditions" }
  ]
}
```

**Step 3: Write Code**
```python
import requests

city = input_city
api_url = f"https://api.weather.com/current/{city}"  # Wrong endpoint!

response = requests.get(api_url)
response.raise_for_status()

data = response.json()
success = True
error_message = ""
temperature = data['temp']
conditions = data['conditions']
```

**Step 4: Test (First Attempt - FAILS)**
```
Tool: execute_backend_step_code
Input: {
  "step_name": "FetchWeatherStep",
  "input_variables": { "city": "London" }
}

Result: {
  "success": false,
  "errorInfo": {
    "errorType": "HTTPError",
    "message": "404 Client Error: Not Found for url: https://api.weather.com/current/London",
    "lineNumber": 6,
    "formattedError": "HTTPError at line 6: 404 Client Error..."
  },
  "recommendations": {
    "items": [
      "Add proper error handling with try/catch blocks",
      "Initialize output variables with default values"
    ]
  }
}
```

**Step 5: Fix Code**
```python
import requests

city = input_city
api_url = f"https://api.weatherapi.com/v1/current.json?key=API_KEY&q={city}"  # Fixed!

try:
    response = requests.get(api_url, timeout=10)
    response.raise_for_status()

    data = response.json()
    success = True
    error_message = ""
    temperature = data['current']['temp_c']
    conditions = data['current']['condition']['text']
except Exception as e:
    success = False
    error_message = str(e)
    temperature = "0"
    conditions = ""
```

**Step 6: Test Again (SUCCESS)**
```
Tool: execute_backend_step_code
Input: {
  "step_name": "FetchWeatherStep",
  "input_variables": { "city": "London" }
}

Result: {
  "success": true,
  "outputVariables": {
    "success": "true",
    "error_message": "",
    "temperature": "15.2",
    "conditions": "Cloudy"
  },
  "recommendations": {
    "followsStudioPatterns": true
  }
}
```

**Step 7: Proceed to Frontend**
```
Tool: save_app_code
Input: { "code": "<html>...</html>" }
```

---

## Error Handling

### Error Types Detected

1. **Syntax Errors**: Python syntax mistakes
2. **Import Errors**: Missing or incorrect module imports
3. **HTTP Errors**: API endpoint failures (404, 500, timeout)
4. **Type Errors**: Variable type mismatches
5. **Attribute Errors**: Accessing non-existent properties
6. **Timeout Errors**: Long-running operations

### Error Information Provided

```json
{
  "errorType": "HTTPError",
  "message": "404 Client Error: Not Found",
  "lineNumber": 6,
  "columnNumber": 12,
  "stackTrace": "Traceback (most recent call last):\n  File ...",
  "formattedError": "HTTPError at line 6: 404 Client Error..."
}
```

### AI Response to Errors

When `execute_backend_step_code` returns `success: false`:

1. **Analyze Error**: Read `errorInfo.formattedError` and `errorInfo.message`
2. **Identify Line**: Check `errorInfo.lineNumber` for specific problem location
3. **Fix Code**: Update the step code to address the error
4. **Re-save**: Call `save_step_code` with fixed code
5. **Re-test**: Call `execute_backend_step_code` again
6. **Repeat**: Continue fix-test cycle until successful

---

## Code Recommendations

The testing tool provides automatic code quality recommendations based on execution results.

### Recommendation Categories

**Error Handling:**
- Add proper error handling with try/catch blocks
- Initialize output variables with default values

**Logging:**
- Add standardized logging with INFO:, WARN:, ERROR: prefixes

**Exception Handling:**
- Add exception handling to make code more robust

**Output Variables:**
- Ensure all expected output variables are assigned at global scope

### Example Recommendations

```json
{
  "recommendations": {
    "items": [
      "Add proper error handling with try/catch blocks",
      "Add standardized logging with INFO:, WARN:, ERROR: prefixes",
      "Ensure all expected output variables are assigned at global scope"
    ],
    "followsStudioPatterns": false
  }
}
```

When `followsStudioPatterns: true`, the code follows best practices.

---

## Tool Reference

### UnifiedAppMcpToolProvider Tools

**Backend Step Development (in workflow order):**

1. **create_backend_step**
   - Creates step and auto-links to app
   - Parameters: `name`, `language`, `description`

2. **suggest_step_variables**
   - Define input/output variables
   - Parameters: `step_name`, `input_variables[]`, `output_variables[]`

3. **save_step_code**
   - Write the step code
   - Parameters: `step_name`, `code`

4. **execute_backend_step_code** ⚠️ CRITICAL
   - Test the step (REQUIRED!)
   - Parameters: `step_name`, `input_variables{}`

5. **get_step_code**
   - Read existing step code
   - Parameters: `step_name`

6. **get_linked_steps**
   - View all steps linked to app
   - Parameters: none

**Frontend Development:**

- **get_current_code** - Get current app HTML/CSS/JS
- **save_app_code** - Save app HTML/CSS/JS

**Reusing Existing Steps (advanced):**

- **link_step_to_app** - Link pre-existing step to current app

---

## Examples

### Example 1: Testing with Empty Input Variables

```json
// Step has defined inputs but AI wants to test with defaults
{
  "step_name": "ProcessDataStep"
  // No input_variables provided
}

// Tool automatically uses defined input variables with empty strings:
{
  "inputVariablesUsed": {
    "data_source": "",
    "filter_type": ""
  }
}
```

### Example 2: Testing with Specific Test Data

```json
{
  "step_name": "CalculateTotalStep",
  "input_variables": {
    "price": "100.50",
    "quantity": "3",
    "tax_rate": "0.08"
  }
}

// Result shows actual calculations:
{
  "outputVariables": {
    "success": "true",
    "subtotal": "301.50",
    "tax": "24.12",
    "total": "325.62"
  }
}
```

### Example 3: Error Detection and Fix Cycle

**Initial Test (FAILS):**
```json
{
  "success": false,
  "errorInfo": {
    "errorType": "NameError",
    "message": "name 'json' is not defined",
    "lineNumber": 15
  }
}
```

**AI Action:**
- Adds `import json` to code
- Re-saves with `save_step_code`
- Re-tests with `execute_backend_step_code`

**Second Test (SUCCESS):**
```json
{
  "success": true,
  "outputVariables": { ... }
}
```

---

## Implementation Details

### Source Code Locations

**UnifiedAppMcpToolProvider**:
- Location: `src/Automaton.Studio/Services/Mcp/ToolProviders/UnifiedAppMcpToolProvider.cs`
- Handler: `HandleExecuteBackendStepCode` (lines 615-729)
- Recommendations: `GetCodeRecommendations` (lines 731-767)

**AppStepsService**:
- Location: `src/Automaton.Studio.Server/Services/AppStepsService.cs`
- Auto-linking: `CreateStepForAppAsync` (lines 38-94)

**ICustomStepExecuteService**:
- Interface: `src/Automaton.Studio/Services/ICustomStepExecuteService.cs`
- Implementation: `src/Automaton.Studio/Services/CustomStepExecuteService.cs`

**System Prompt**:
- Location: `src/Automaton.Studio/Services/Mcp/SystemPrompts/UnifiedAppSystemPromptBuilder.cs`
- Testing section: `GetWorkflowSection` (lines 87-119)
- Tool sequence: `GetToolSequenceSection` (lines 135-152)

### Testing Configuration

**Execution Service**: Uses same `ICustomStepExecuteService` as Runner for consistency

**Timeout**: Inherits from `CustomStepExecuteService` configuration

**Isolation**: Each test runs in isolated Python context (via pythonnet)

**Output Filtering**: Only returns output variables defined in step definition

---

## Best Practices

### For AI Integration

1. **Always Test**: Never finalize an app without testing backend steps
2. **Use Realistic Data**: Test with realistic input values, not empty strings
3. **Handle Errors**: Analyze error messages and fix systematically
4. **Iterate**: Use fix-test cycles until successful
5. **Follow Recommendations**: Apply code quality suggestions

### For Developers

1. **Trust the Service**: `ICustomStepExecuteService` matches Runner execution
2. **Check Recommendations**: Review AI-generated code quality suggestions
3. **Monitor Logs**: Execution logs show INFO/WARN/ERROR for debugging
4. **Validate Output**: Ensure all expected output variables are assigned

### For System Administrators

1. **Configure Timeout**: Set appropriate timeout in `CustomStepExecuteService`
2. **Python Environment**: Ensure Python runtime is accessible
3. **Module Availability**: Install commonly used Python packages
4. **Monitor Usage**: Track execution frequency and errors

---

## Related Documentation

- **MCP Architecture**: `docs/mcp/MCP_ARCHITECTURE_SPECIFICATIONS.md`
- **Implementation Plan**: `IMPLEMENTATION_PLAN_MCP_IMPROVEMENTS.md` (root directory)
- **Project Overview**: `CLAUDE.md` (root directory)
- **Apps Feature**: `docs/webapps/` directory

---

## Changelog

**January 2026**:
- Initial documentation created
- `execute_backend_step_code` tool implemented
- Auto-linking feature documented
- Code recommendations system documented

---

## Support

For issues or questions about backend step testing:
1. Check error messages in execution results
2. Review recommendations provided by the tool
3. Consult this documentation
4. Check implementation code for detailed behavior
