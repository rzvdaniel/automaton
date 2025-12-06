# API Reference

## Overview

This document provides the complete API reference for the unified App + Backend Step generation feature.

---

## Base URLs

- **Development**: `https://localhost:7091`
- **Production**: Configured via `ConfigurationService.BaseUrl`

---

## Authentication

Most endpoints require authentication via:
- **JWT Bearer Token**: `Authorization: Bearer <token>`
- **API Key**: `X-Api-Key: <key>` (for certain endpoints)

Web App execution endpoints support both authenticated and anonymous access based on app configuration.

---

## Step Execution API

### Execute Step from App

Execute a Custom Step directly from a published Web App.

```http
POST /api/web-apps/{appSlug}/steps/{stepIdentifier}/execute
```

#### Path Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `appSlug` | string | The app's public URL slug |
| `stepIdentifier` | string | Step name or GUID |

#### Request Body

```json
{
  "inputs": {
    "variable_name": "value",
    "api_key": "secret_value"
  },
  "runnerNames": ["runner-1", "runner-2"]
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `inputs` | object | No | Input variable values as key-value pairs |
| `runnerNames` | string[] | No | Specific runners to execute on (uses app default if not specified) |

#### Response

**Success (200 OK)**

```json
{
  "success": true,
  "stepId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "stepName": "SendEmailStep",
  "status": "Completed",
  "runnerCount": 1,
  "executedAt": "2025-01-15T10:30:00Z",
  "completedAt": "2025-01-15T10:30:02Z",
  "outputVariables": {
    "success": true,
    "message_id": "msg_123456",
    "error_message": ""
  },
  "logs": [
    "[10:30:00] Starting step execution for app 'MyApp'",
    "[10:30:00] Resolved step: SendEmailStep (ID: 3fa85f64...)",
    "[10:30:01] Using 1 runner(s)",
    "[runner-1] INFO: Sending email to user@example.com",
    "[runner-1] INFO: Email sent successfully",
    "[10:30:02] Execution completed. Success: True"
  ]
}
```

**Error Responses**

| Status | Condition | Response |
|--------|-----------|----------|
| 400 | Invalid request | `{"error": "Invalid input format"}` |
| 401 | Auth required but missing | `{"error": "Authentication required"}` |
| 404 | App or step not found | `{"error": "App not found or not published"}` |
| 500 | Execution failed | `{"error": "Internal server error during step execution"}` |

#### Example Usage (JavaScript)

```javascript
const result = await automatonApi.executeStep('SendEmailStep', {
  inputs: {
    to_email: 'user@example.com',
    subject: 'Hello',
    body: 'This is a test email'
  }
});

if (result.success && result.outputVariables?.success) {
  console.log('Email sent! Message ID:', result.outputVariables.message_id);
} else {
  console.error('Failed:', result.outputVariables?.error_message || result.error);
}
```

---

### List Steps for App

Get all steps available for a published app.

```http
GET /api/web-apps/{appSlug}/steps
```

#### Response

**Success (200 OK)**

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "SendEmailStep",
    "displayName": "Send Email",
    "description": "Sends emails using SendGrid API",
    "language": "Python"
  },
  {
    "id": "7fb92c87-1234-5678-abcd-987654321fed",
    "name": "ProcessDataStep",
    "displayName": "Process Data",
    "description": "Processes and transforms data",
    "language": "Python"
  }
]
```

---

## App Steps Management API

### Get Linked Steps

Get all Custom Steps linked to an app.

```http
GET /api/apps/{appId}/steps
```

**Requires Authentication**

#### Response

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "SendEmailStep",
    "displayName": "Send Email",
    "description": "Sends emails using SendGrid API",
    "language": "Python",
    "version": 3,
    "created": "2025-01-10T08:00:00Z",
    "updated": "2025-01-14T16:30:00Z",
    "linkedAt": "2025-01-12T12:00:00Z",
    "displayOrder": 0,
    "inputCount": 4,
    "outputCount": 3,
    "inputVariables": [
      {"name": "api_key", "description": "SendGrid API key", "type": "Text", "isSensitive": true},
      {"name": "to_email", "description": "Recipient email", "type": "Text", "isSensitive": false},
      {"name": "subject", "description": "Email subject", "type": "Text", "isSensitive": false},
      {"name": "body", "description": "Email body", "type": "Text", "isSensitive": false}
    ],
    "outputVariables": [
      {"name": "success", "description": "Whether email was sent", "type": "Boolean"},
      {"name": "message_id", "description": "SendGrid message ID", "type": "Text"},
      {"name": "error_message", "description": "Error if failed", "type": "Text"}
    ]
  }
]
```

---

### Get Available Steps

Get steps that can be linked to an app (not already linked).

```http
GET /api/apps/{appId}/steps/available
```

**Requires Authentication**

#### Response

Same format as Get Linked Steps, but excludes already-linked steps.

---

### Link Step to App

Create a link between an app and a Custom Step.

```http
POST /api/apps/{appId}/steps/{stepId}/link
```

**Requires Authentication**

#### Response

- **204 No Content**: Successfully linked
- **400 Bad Request**: Invalid request
- **401 Unauthorized**: Access denied
- **404 Not Found**: App or step not found

---

### Unlink Step from App

Remove the link between an app and a Custom Step.

```http
DELETE /api/apps/{appId}/steps/{stepId}
```

**Requires Authentication**

#### Response

- **204 No Content**: Successfully unlinked
- **401 Unauthorized**: Access denied
- **404 Not Found**: Link not found

---

## automatonApi JavaScript SDK

The SDK is automatically injected into published web apps.

### executeStep

Execute a Custom Step.

```javascript
automatonApi.executeStep(stepIdentifier, options)
```

#### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `stepIdentifier` | string | Step name or GUID |
| `options.inputs` | object | Input variable values |
| `options.runnerNames` | string[] | Specific runners (optional) |

#### Returns

`Promise<WebAppStepExecutionResponse>`

#### Example

```javascript
try {
  const result = await automatonApi.executeStep('ProcessDataStep', {
    inputs: {
      data_source: 'https://api.example.com/data',
      output_format: 'json'
    },
    runnerNames: ['production-runner']
  });
  
  console.log('Processed:', result.outputVariables);
} catch (error) {
  console.error('Execution failed:', error.message);
}
```

---

### executeFlow (Existing)

Execute a Flow (existing functionality).

```javascript
automatonApi.executeFlow(flowIdentifier, options)
```

#### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `flowIdentifier` | string | Flow name or GUID |
| `options.inputs` | object | Input variable values |
| `options.runnerNames` | string[] | Specific runners (optional) |

---

### customCall

Make a custom API call through the secure proxy.

```javascript
automatonApi.customCall(path, method, body)
```

#### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `path` | string | API path (must start with `/api/web-apps/`) |
| `method` | string | HTTP method (`GET` or `POST`) |
| `body` | object | Request body (for POST) |

---

### getAppSlug

Get the current app's public URL slug.

```javascript
const slug = automatonApi.getAppSlug();
// Returns: 'my-app-slug'
```

---

## MCP Tools (AI Context)

These tools are available to the AI when generating unified App + Step code.

### Frontend Tools

| Tool | Description | Arguments |
|------|-------------|-----------|
| `get_current_code` | Get existing app HTML/CSS/JS | None |
| `save_app_code` | Save frontend code | `code`: string |

### Backend Tools

| Tool | Description | Arguments |
|------|-------------|-----------|
| `create_backend_step` | Create new Custom Step | `name`, `language`, `description` |
| `get_step_code` | Get step's current code | `step_name` |
| `save_step_code` | Save code to step | `step_name`, `code` |
| `suggest_step_variables` | Define inputs/outputs | `step_name`, `input_variables`, `output_variables` |
| `execute_step_code` | Test step execution | `step_name`, `inputs` |
| `link_step_to_app` | Link step to current app | `step_name` |
| `get_linked_steps` | List linked steps | None |

### Tool Response Format

```json
{
  "content": [
    {
      "type": "text",
      "text": "Tool execution result message"
    }
  ],
  "isError": false
}
```

---

## Error Codes

| Code | HTTP Status | Description |
|------|-------------|-------------|
| `APP_NOT_FOUND` | 404 | App does not exist or is not published |
| `STEP_NOT_FOUND` | 404 | Step does not exist |
| `AUTH_REQUIRED` | 401 | Authentication required but not provided |
| `ACCESS_DENIED` | 403 | User does not have permission |
| `RUNNER_NOT_AVAILABLE` | 500 | No runners available for execution |
| `EXECUTION_FAILED` | 500 | Step execution failed on runner |
| `INVALID_INPUT` | 400 | Invalid request format or parameters |

---

## Rate Limiting

| Endpoint Type | Limit |
|---------------|-------|
| Step Execution (public) | 60 requests/minute per app |
| Step Execution (authenticated) | 120 requests/minute per user |
| API Management | 100 requests/minute per user |

Rate limit headers are included in responses:
- `X-RateLimit-Limit`: Maximum requests allowed
- `X-RateLimit-Remaining`: Requests remaining
- `X-RateLimit-Reset`: Unix timestamp when limit resets

---

## WebSocket Events (SignalR)

Step execution uses SignalR for runner communication.

### Hub Methods

| Method | Direction | Description |
|--------|-----------|-------------|
| `RunCustomStep` | Server ? Runner | Execute step on runner |

### Response Format

```csharp
public class CustomStepExecutionResponse
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public DateTime Started { get; set; }
    public DateTime Finished { get; set; }
    public Dictionary<string, string>? OutputVariables { get; set; }
}
```

---

## Versioning

The API uses URL-based versioning where needed:

- Current version: v1 (implicit in paths)
- Future versions: `/api/v2/...`

Backward compatibility is maintained for all existing endpoints.
