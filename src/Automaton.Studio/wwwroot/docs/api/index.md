# API Reference

The Automaton API provides programmatic access to all platform functionality, enabling integration with external systems and custom tooling.

## Base Information

- **Base URL**: `https://your-studio-url:7091/api`
- **Authentication**: API Key-based authentication
- **Content Type**: `application/json`
- **API Version**: v1

## Authentication

All API requests require an API key passed in the request header:

```http
Authorization: Bearer ak_your_api_key_here
```

### API Key Types

| Key Format | Description | Permissions |
|------------|-------------|-------------|
| `ak_runner_xxx` | Runner keys | Execute flows, update status |
| `ak_execute_xxx` | Execution keys | Execute flows only |
| `ak_readonly_xxx` | Read-only keys | View flows, logs, runners |
| `ak_admin_xxx` | Admin keys | Full access to all resources |

## API Endpoints

### Flows API

#### List Flows
```http
GET /api/flows
```

**Response:**
```json
{
  "data": [
    {
      "id": "flow-uuid",
      "name": "My Automation Flow",
      "description": "Processes daily reports",
      "isActive": true,
      "createdAt": "2024-01-15T10:30:00Z",
      "updatedAt": "2024-01-15T15:45:00Z",
      "tags": ["reporting", "daily"]
    }
  ],
  "total": 1,
  "page": 1,
  "pageSize": 50
}
```

#### Get Flow Details
```http
GET /api/flows/{flowId}
```

#### Create Flow
```http
POST /api/flows
Content-Type: application/json

{
  "name": "New Flow",
  "description": "Flow description",
  "definition": {
    "steps": [...],
    "connections": [...]
  }
}
```

#### Execute Flow
```http
POST /api/flows/{flowId}/execute
Content-Type: application/json

{
  "runnerId": "runner-uuid",
  "parameters": {
    "inputParam1": "value1",
    "inputParam2": "value2"
  }
}
```

### Runners API

#### List Runners
```http
GET /api/runners
```

**Response:**
```json
{
  "data": [
    {
      "id": "runner-uuid",
      "name": "Production Runner 1",
      "status": "online",
      "lastHeartbeat": "2024-01-15T16:00:00Z",
      "version": "1.0.0",
      "capabilities": ["python", "http", "file"],
      "currentJobs": 2,
      "maxJobs": 5
    }
  ]
}
```

#### Get Runner Status
```http
GET /api/runners/{runnerId}/status
```

#### Send Command to Runner
```http
POST /api/runners/{runnerId}/command
Content-Type: application/json

{
  "command": "execute_flow",
  "flowId": "flow-uuid",
  "parameters": {}
}
```

### Executions API

#### List Executions
```http
GET /api/executions?flowId={flowId}&status={status}&page={page}
```

#### Get Execution Details
```http
GET /api/executions/{executionId}
```

**Response:**
```json
{
  "id": "execution-uuid",
  "flowId": "flow-uuid",
  "runnerId": "runner-uuid",
  "status": "completed",
  "startTime": "2024-01-15T16:00:00Z",
  "endTime": "2024-01-15T16:05:30Z",
  "duration": "00:05:30",
  "steps": [
    {
      "stepId": "step-1",
      "name": "HTTP Request",
      "status": "completed",
      "startTime": "2024-01-15T16:00:00Z",
      "endTime": "2024-01-15T16:00:02Z",
      "output": {...}
    }
  ]
}
```

#### Get Execution Logs
```http
GET /api/executions/{executionId}/logs
```

### Steps API

#### List Available Steps
```http
GET /api/steps
```

#### Get Step Definition
```http
GET /api/steps/{stepId}
```

#### Create Custom Step
```http
POST /api/steps
Content-Type: application/json

{
  "name": "My Custom Step",
  "category": "Custom",
  "description": "Custom step description",
  "inputSchema": {...},
  "outputSchema": {...},
  "code": "# Python code here"
}
```

## WebSocket API (SignalR)

For real-time updates, connect to the SignalR hub:

**Connection URL**: `wss://your-studio-url:7091/hubs/automaton`

### Hub Methods

#### Join Groups
```javascript
connection.invoke("JoinGroup", "flows");
connection.invoke("JoinGroup", "executions");
```

#### Receive Updates
```javascript
connection.on("FlowUpdated", (flow) => {
    console.log("Flow updated:", flow);
});

connection.on("ExecutionStatusChanged", (execution) => {
    console.log("Execution status:", execution.status);
});
```

## Error Handling

The API uses standard HTTP status codes:

- `200` - Success
- `201` - Created
- `400` - Bad Request (validation error)
- `401` - Unauthorized (missing/invalid API key)
- `403` - Forbidden (insufficient permissions)
- `404` - Not Found
- `409` - Conflict (resource already exists)
- `500` - Internal Server Error

**Error Response Format:**
```json
{
  "error": {
    "code": "ValidationError",
    "message": "One or more validation errors occurred.",
    "details": [
      {
        "field": "name",
        "message": "Name is required"
      }
    ]
  }
}
```

## Rate Limiting

- **Rate Limit**: 1000 requests per hour per API key
- **Headers**: 
  - `X-RateLimit-Limit`: Maximum requests per hour
  - `X-RateLimit-Remaining`: Remaining requests
  - `X-RateLimit-Reset`: Time when limit resets

## SDK Examples

### JavaScript/Node.js
```javascript
const axios = require('axios');

const api = axios.create({
  baseURL: 'https://your-studio-url:7091/api',
  headers: {
    'Authorization': 'Bearer ak_your_api_key',
    'Content-Type': 'application/json'
  }
});

// Execute a flow
async function executeFlow(flowId, parameters) {
  try {
    const response = await api.post(`/flows/${flowId}/execute`, {
      parameters: parameters
    });
    return response.data;
  } catch (error) {
    console.error('Error executing flow:', error.response.data);
    throw error;
  }
}
```

### Python
```python
import requests

class AutomatonAPI:
    def __init__(self, base_url, api_key):
        self.base_url = base_url
        self.headers = {
            'Authorization': f'Bearer {api_key}',
            'Content-Type': 'application/json'
        }
    
    def execute_flow(self, flow_id, parameters=None):
        url = f"{self.base_url}/api/flows/{flow_id}/execute"
        payload = {'parameters': parameters or {}}
        
        response = requests.post(url, json=payload, headers=self.headers)
        response.raise_for_status()
        return response.json()

# Usage
api = AutomatonAPI('https://your-studio-url:7091', 'ak_your_api_key')
result = api.execute_flow('flow-uuid', {'param1': 'value1'})
```

### C#
```csharp
using System.Text.Json;
using System.Net.Http.Headers;

public class AutomatonApiClient
{
    private readonly HttpClient _httpClient;

    public AutomatonApiClient(string baseUrl, string apiKey)
    {
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<ExecutionResult> ExecuteFlowAsync(string flowId, 
        Dictionary<string, object> parameters)
    {
        var payload = new { parameters = parameters };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"/api/flows/{flowId}/execute", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ExecutionResult>(responseJson);
    }
}
```

## OpenAPI/Swagger Documentation

Interactive API documentation is available at:
- **Development**: `https://localhost:7091/swagger`
- **Production**: `https://your-studio-url:7091/swagger`

The OpenAPI specification can be accessed at:
- `https://your-studio-url:7091/swagger/v1/swagger.json`

## Next Steps

- Explore specific API endpoints for [Flows](flows.md), [Runners](runners.md), and [Steps](steps.md)
- Learn about [Authentication](authentication.md) and security best practices
- Check out [Integration Examples](../reference/integration-examples.md) for common scenarios