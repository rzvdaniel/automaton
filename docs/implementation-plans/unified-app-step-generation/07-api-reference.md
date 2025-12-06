# API Reference

## Overview

This document provides the complete API reference for the unified App + Backend Step generation feature, including both direct step execution and flow execution capabilities.

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

## Flow Execution API (Existing + Enhanced)

### Execute Flow from App

Execute a Flow (multi-step workflow) from a published Web App.

```http
POST /api/web-apps/{appSlug}/flows/{flowIdentifier}/execute
```

#### Path Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `appSlug` | string | The app's public URL slug |
| `flowIdentifier` | string | Flow name or GUID |

#### Request Body

```json
{
  "inputs": {
    "user_name": "John Doe",
    "user_email": "john@example.com",
    "api_key": "sk_..."
  },
  "runnerNames": ["runner-1"]
}
```

#### Response

**Success (200 OK)**

```json
{
  "success": true,
  "flowId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "flowName": "UserRegistrationFlow",
  "status": "Completed",
  "runnerCount": 1,
  "executedAt": "2025-01-15T10:30:00Z",
  "completedAt": "2025-01-15T10:30:05Z",
  "outputVariables": {
    "user_id": "usr_789",
    "email_id": "msg_456",
    "log_id": "log_123",
    "success": true
  },
  "logs": [
    "[10:30:00] Starting flow: UserRegistrationFlow",
    "[10:30:01] Step 1/3: CreateUserStep - Started",
    "[10:30:02] Step 1/3: CreateUserStep - Completed (user_id: usr_789)",
    "[10:30:02] Step 2/3: SendWelcomeEmailStep - Started",
    "[10:30:04] Step 2/3: SendWelcomeEmailStep - Completed (email_id: msg_456)",
    "[10:30:04] Step 3/3: CreateAuditLogStep - Started",
    "[10:30:05] Step 3/3: CreateAuditLogStep - Completed",
    "[10:30:05] Flow completed successfully"
  ]
}
```

#### Example Usage (JavaScript)

```javascript
// For complex multi-step operations, use executeFlow
const result = await automatonApi.executeFlow('UserRegistrationFlow', {
  inputs: {
    user_name: 'John Doe',
    user_email: 'john@example.com',
    password: 'hashed_password_here'
  }
});

if (result.success) {
  console.log('Registration complete!');
  console.log('User ID:', result.outputVariables.user_id);
  console.log('Welcome email sent:', result.outputVariables.email_id);
} else {
  console.error('Registration failed:', result.error);
}
```

---

### List Flows for App

Get all flows available for a published app.

```http
GET /api/web-apps/{appSlug}/flows
```

#### Response

**Success (200 OK)**

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "UserRegistrationFlow",
    "description": "Complete user registration with email and audit log",
    "stepCount": 3,
    "inputVariables": ["user_name", "user_email", "password"],
    "outputVariables": ["user_id", "email_id", "log_id", "success"]
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

## App Flows Management API (Phase 6)

### Get Linked Flows

Get all Flows linked to an app.

```http
GET /api/apps/{appId}/flows
```

**Requires Authentication**

#### Response

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "UserRegistrationFlow",
    "description": "Complete user registration workflow",
    "stepCount": 3,
    "steps": ["CreateUserStep", "SendWelcomeEmailStep", "CreateAuditLogStep"],
    "created": "2025-01-10T08:00:00Z",
    "updated": "2025-01-14T16:30:00Z",
    "linkedAt": "2025-01-12T12:00:00Z"
  }
]
```

---

### Link Flow to App

Create a link between an app and a Flow.

```http
POST /api/apps/{appId}/flows/{flowId}/link
```

**Requires Authentication**

---

### Unlink Flow from App

Remove the link between an app and a Flow.

```http
DELETE /api/apps/{appId}/flows/{flowId}
```

**Requires Authentication**

---

## automatonApi JavaScript SDK

The SDK is automatically injected into published web apps.

### executeStep

Execute a Custom Step (for simple single-operation backends).

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
// Simple backend operation - use executeStep
const result = await automatonApi.executeStep('SendEmailStep', {
  inputs: {
    to_email: 'user@example.com',
    subject: 'Hello',
    body: 'Message content'
  }
});
```

---

### executeFlow

Execute a Flow (for complex multi-step backends).

```javascript
automatonApi.executeFlow(flowIdentifier, options)
```

#### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `flowIdentifier` | string | Flow name or GUID |
| `options.inputs` | object | Input variable values |
| `options.runnerNames` | string[] | Specific runners (optional) |

#### Returns

`Promise<WebAppFlowExecutionResponse>`

#### Example

```javascript
// Complex multi-step operation - use executeFlow
const result = await automatonApi.executeFlow('UserRegistrationFlow', {
  inputs: {
    user_name: 'John Doe',
    user_email: 'john@example.com',
    password: 'hashed_password'
  }
});

// Access outputs from multiple steps
console.log('User ID:', result.outputVariables.user_id);
console.log('Email ID:', result.outputVariables.email_id);
```

---

### When to Use executeStep vs executeFlow

| Use Case | Method | Example |
|----------|--------|---------|
| Single API call | `executeStep` | Send email, process payment |
| Single DB operation | `executeStep` | Create record, update status |
| Independent operations | Multiple `executeStep` | Two unrelated buttons |
| Dependent operations | `executeFlow` | Create user ? send email |
| Transactional operations | `executeFlow` | Payment ? inventory ? receipt |
| Operations with rollback | `executeFlow` | Complex workflows |

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

These tools are available to the AI when generating unified App + Step/Flow code.

### Frontend Tools

| Tool | Description | Arguments |
|------|-------------|-----------|
| `get_current_code` | Get existing app HTML/CSS/JS | None |
| `save_app_code` | Save frontend code | `code`: string |

### Backend Step Tools

| Tool | Description | Arguments |
|------|-------------|-----------|
| `create_backend_step` | Create new Custom Step | `name`, `language`, `description` |
| `get_step_code` | Get step's current code | `step_name` |
| `save_step_code` | Save code to step | `step_name`, `code` |
| `suggest_step_variables` | Define inputs/outputs | `step_name`, `input_variables`, `output_variables` |
| `execute_step_code` | Test step execution | `step_name`, `inputs` |
| `link_step_to_app` | Link step to current app | `step_name` |
| `get_linked_steps` | List linked steps | None |

### Backend Flow Tools (Phase 6)

| Tool | Description | Arguments |
|------|-------------|-----------|
| `create_flow` | Create new Flow | `name`, `description` |
| `add_step_to_flow` | Add step to flow | `flow_name`, `step_name`, `position`, `mappings` |
| `configure_flow_variables` | Set flow I/O | `flow_name`, `input_variables`, `output_variables` |
| `link_flow_to_app` | Link flow to current app | `flow_name` |
| `get_linked_flows` | List linked flows | None |

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
| `FLOW_NOT_FOUND` | 404 | Flow does not exist |
| `AUTH_REQUIRED` | 401 | Authentication required but not provided |
| `ACCESS_DENIED` | 403 | User does not have permission |
| `RUNNER_NOT_AVAILABLE` | 500 | No runners available for execution |
| `EXECUTION_FAILED` | 500 | Step/Flow execution failed on runner |
| `INVALID_INPUT` | 400 | Invalid request format or parameters |

---

## Rate Limiting

| Endpoint Type | Limit |
|---------------|-------|
| Step Execution (public) | 60 requests/minute per app |
| Step Execution (authenticated) | 120 requests/minute per user |
| Flow Execution (public) | 30 requests/minute per app |
| Flow Execution (authenticated) | 60 requests/minute per user |
| API Management | 100 requests/minute per user |

Rate limit headers are included in responses:
- `X-RateLimit-Limit`: Maximum requests allowed
- `X-RateLimit-Remaining`: Requests remaining
- `X-RateLimit-Reset`: Unix timestamp when limit resets

---

## WebSocket Events (SignalR)

Step and Flow execution uses SignalR for runner communication.

### Hub Methods

| Method | Direction | Description |
|--------|-----------|-------------|
| `RunCustomStep` | Server ? Runner | Execute step on runner |
| `RunFlow` | Server ? Runner | Execute flow on runner |

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
