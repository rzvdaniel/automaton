# SignalR Real-Time Communication

Patterns and code examples for implementing SignalR real-time communication between server, runners, and frontend.

## Overview

Automaton uses SignalR for real-time bidirectional communication:
- **RunnerHub**: Runner registration, heartbeat, and command dispatch
- **FlowExecutionHub**: Real-time execution progress and log streaming

## Runner Hub (Backend)

### Runner Registration and Command Dispatch

```csharp
// Automaton.Studio.Server/Hubs/RunnerHub.cs
using Microsoft.AspNetCore.SignalR;

public class RunnerHub : Hub
{
    private readonly ILogger<RunnerHub> _logger;
    private readonly IRunnerService _runnerService;

    public RunnerHub(ILogger<RunnerHub> logger, IRunnerService runnerService)
    {
        _logger = logger;
        _runnerService = runnerService;
    }

    public async Task RegisterRunner(string runnerId, string version)
    {
        // Add runner to tracking
        await _runnerService.RegisterRunnerAsync(runnerId, version, Context.ConnectionId);
        
        // Add to SignalR group
        await Groups.AddToGroupAsync(Context.ConnectionId, runnerId);
        
        // Notify all clients
        await Clients.All.SendAsync("RunnerOnline", runnerId, version);
        
        _logger.LogInformation("Runner {RunnerId} registered with version {Version}", runnerId, version);
    }

    public async Task UpdateHeartbeat(string runnerId)
    {
        await _runnerService.UpdateHeartbeatAsync(runnerId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Get runner ID from connection
        var runnerId = await _runnerService.GetRunnerIdByConnectionAsync(Context.ConnectionId);
        
        if (!string.IsNullOrEmpty(runnerId))
        {
            await _runnerService.UnregisterRunnerAsync(runnerId);
            await Clients.All.SendAsync("RunnerOffline", runnerId);
            _logger.LogInformation("Runner {RunnerId} disconnected", runnerId);
        }
        
        await base.OnDisconnectedAsync(exception);
    }
}
```

### Dispatching Execution Commands to Runners

```csharp
// From a service or controller
public class FlowExecutionService
{
    private readonly IHubContext<RunnerHub> _runnerHubContext;

    public async Task<Guid> ExecuteFlowOnRunnerAsync(Guid flowId, string runnerId, Dictionary<string, object> inputs)
    {
        var executionId = Guid.NewGuid();
        
        // Send execution command to specific runner
        await _runnerHubContext.Clients.Group(runnerId).SendAsync(
            "ExecuteFlow", 
            executionId,
            flowId, 
            inputs
        );
        
        return executionId;
    }
}
```

## Flow Execution Hub (Backend)

### Streaming Execution Progress

```csharp
// Automaton.Studio.Server/Hubs/FlowExecutionHub.cs
using Microsoft.AspNetCore.SignalR;

public class FlowExecutionHub : Hub
{
    private readonly ILogger<FlowExecutionHub> _logger;

    public FlowExecutionHub(ILogger<FlowExecutionHub> logger)
    {
        _logger = logger;
    }

    // Runner sends execution progress
    public async Task ReportProgress(Guid executionId, string stepName, string status, List<string> logs)
    {
        // Broadcast to all frontend clients monitoring this execution
        await Clients.All.SendAsync("ExecutionProgress", executionId, stepName, status, logs);
        
        _logger.LogDebug("Execution {ExecutionId}: {StepName} - {Status}", executionId, stepName, status);
    }

    // Runner sends final execution result
    public async Task ReportCompletion(Guid executionId, string status, Dictionary<string, object> outputs)
    {
        await Clients.All.SendAsync("ExecutionCompleted", executionId, status, outputs);
        
        _logger.LogInformation("Execution {ExecutionId} completed with status {Status}", executionId, status);
    }

    // Join execution room for specific execution monitoring
    public async Task JoinExecutionRoom(Guid executionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"execution_{executionId}");
    }

    public async Task LeaveExecutionRoom(Guid executionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"execution_{executionId}");
    }
}
```

## Frontend (Vue.js/TypeScript)

### SignalR Client Setup

```typescript
// src/shared/services/signalr/signalr-client.ts
import * as signalR from '@microsoft/signalr';

class SignalRClient {
  private connection: signalR.HubConnection | null = null;
  private executionConnection: signalR.HubConnection | null = null;

  async connectToFlowExecutionHub(): Promise<void> {
    this.executionConnection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/flow-execution', {
        accessTokenFactory: () => {
          // Get JWT token from storage
          return localStorage.getItem('authToken') || '';
        }
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    // Set up event handlers
    this.executionConnection.on('ExecutionProgress', (executionId, stepName, status, logs) => {
      console.log(`Execution ${executionId}: ${stepName} - ${status}`);
      // Update UI/store with progress
    });

    this.executionConnection.on('ExecutionCompleted', (executionId, status, outputs) => {
      console.log(`Execution ${executionId} completed: ${status}`);
      // Update UI/store with final result
    });

    // Handle reconnection
    this.executionConnection.onreconnecting((error) => {
      console.warn('SignalR reconnecting...', error);
    });

    this.executionConnection.onreconnected((connectionId) => {
      console.info('SignalR reconnected:', connectionId);
    });

    this.executionConnection.onclose((error) => {
      console.error('SignalR connection closed:', error);
    });

    await this.executionConnection.start();
    console.info('Connected to FlowExecutionHub');
  }

  async joinExecutionRoom(executionId: string): Promise<void> {
    if (!this.executionConnection) {
      throw new Error('Not connected to FlowExecutionHub');
    }
    await this.executionConnection.invoke('JoinExecutionRoom', executionId);
  }

  async leaveExecutionRoom(executionId: string): Promise<void> {
    if (!this.executionConnection) return;
    await this.executionConnection.invoke('LeaveExecutionRoom', executionId);
  }

  async disconnect(): Promise<void> {
    if (this.executionConnection) {
      await this.executionConnection.stop();
      this.executionConnection = null;
    }
  }
}

export const signalRClient = new SignalRClient();
```

### Using SignalR in Vue Components

```vue
<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import { signalRClient } from '@/shared/services/signalr/signalr-client';

const executionLogs = ref<string[]>([]);
const executionStatus = ref<string>('');
const executionId = ref<string>('');

onMounted(async () => {
  await signalRClient.connectToFlowExecutionHub();
  
  // Join specific execution room
  if (executionId.value) {
    await signalRClient.joinExecutionRoom(executionId.value);
  }
});

onUnmounted(async () => {
  if (executionId.value) {
    await signalRClient.leaveExecutionRoom(executionId.value);
  }
  await signalRClient.disconnect();
});
</script>

<template>
  <div>
    <h3>Execution Status: {{ executionStatus }}</h3>
    <ul>
      <li v-for="(log, index) in executionLogs" :key="index">{{ log }}</li>
    </ul>
  </div>
</template>
```

## Runner (Console Client)

### Runner SignalR Client

```csharp
// Automaton.Runner/Services/RunnerSignalRClient.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

public class RunnerSignalRClient
{
    private readonly ILogger<RunnerSignalRClient> _logger;
    private readonly string _serverUrl;
    private readonly string _apiKey;
    private HubConnection? _connection;

    public RunnerSignalRClient(ILogger<RunnerSignalRClient> logger, string serverUrl, string apiKey)
    {
        _logger = logger;
        _serverUrl = serverUrl;
        _apiKey = apiKey;
    }

    public async Task ConnectAsync()
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{_serverUrl}/hubs/runner", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(_apiKey)!;
            })
            .WithAutomaticReconnect()
            .Build();

        // Handle execution commands from server
        _connection.On<Guid, Guid, Dictionary<string, object>>("ExecuteFlow", 
            async (executionId, flowId, inputs) =>
            {
                _logger.LogInformation("Received execution command for flow {FlowId}", flowId);
                await ExecuteFlowAsync(executionId, flowId, inputs);
            });

        _connection.Reconnecting += error =>
        {
            _logger.LogWarning("Reconnecting to server... {Error}", error?.Message);
            return Task.CompletedTask;
        };

        _connection.Reconnected += connectionId =>
        {
            _logger.LogInformation("Reconnected to server with connection ID {ConnectionId}", connectionId);
            return RegisterRunnerAsync();
        };

        await _connection.StartAsync();
        await RegisterRunnerAsync();
        
        _logger.LogInformation("Connected to RunnerHub");
    }

    private async Task RegisterRunnerAsync()
    {
        if (_connection?.State == HubConnectionState.Connected)
        {
            var runnerId = Environment.MachineName;
            var version = "1.0.0";
            await _connection.InvokeAsync("RegisterRunner", runnerId, version);
        }
    }

    private async Task ExecuteFlowAsync(Guid executionId, Guid flowId, Dictionary<string, object> inputs)
    {
        // Execute flow logic...
        // Report progress back to server
        await ReportProgressAsync(executionId, "StepName", "Running", new List<string> { "Step started" });
    }

    public async Task ReportProgressAsync(Guid executionId, string stepName, string status, List<string> logs)
    {
        if (_connection?.State == HubConnectionState.Connected)
        {
            // Connect to FlowExecutionHub and report
            var executionHub = new HubConnectionBuilder()
                .WithUrl($"{_serverUrl}/hubs/flow-execution", options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(_apiKey)!;
                })
                .Build();

            await executionHub.StartAsync();
            await executionHub.InvokeAsync("ReportProgress", executionId, stepName, status, logs);
            await executionHub.StopAsync();
        }
    }

    public async Task DisconnectAsync()
    {
        if (_connection != null)
        {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
        }
    }
}
```

## Best Practices

1. **Always use automatic reconnection** - `WithAutomaticReconnect()` handles network interruptions
2. **Implement heartbeat mechanisms** - Keep connections alive and detect dead runners
3. **Use groups for targeted messaging** - Avoid broadcasting to all clients when possible
4. **Handle connection state changes** - Reconnecting, reconnected, closed events
5. **Log all SignalR events** - Critical for debugging real-time issues
6. **Secure with authentication** - Use JWT tokens or API keys via `AccessTokenProvider`
7. **Clean up connections** - Always stop and dispose connections in `onUnmounted`/`Dispose`

## Troubleshooting

### Connection Failures
- Verify server URL and hub endpoint paths
- Check authentication tokens are valid
- Review CORS configuration on backend
- Inspect browser console for WebSocket errors

### Message Delivery Issues
- Ensure clients are in correct groups
- Verify method names match on both ends (case-sensitive)
- Check parameter types match expectations
- Review server logs for invocation errors

### Performance
- Use groups to reduce broadcast overhead
- Avoid sending large payloads (>1MB) through SignalR
- Consider compression for large messages
- Monitor connection count and memory usage

## Reference

- SignalR Documentation: https://learn.microsoft.com/en-us/aspnet/core/signalr/
- Hub Configuration: `src/Automaton.Studio.Server/Program.cs`
- Runner Implementation: `src/Automaton.Runner/Services/`
