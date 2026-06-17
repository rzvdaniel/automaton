# Runner HTTP Tunnel — Implementation Plan

## Goal

Replace the current SignalR RPC model for app controller execution with an HTTP tunnel.
The Runner keeps its WebSocket connection to the server (NAT traversal, service discovery) but
stops being a Python executor. Instead it forwards HTTP requests to a local FastAPI process
running inside the same Docker container.

This enables:
- True concurrent app backend execution (hundreds of requests in-flight simultaneously)
- Full async Python (SQLAlchemy, httpx, etc.)
- Process isolation (Python crash ≠ Runner crash)
- No GIL serialization for I/O-bound workloads
**Flows/steps remain on the current SignalR RPC path — this plan only touches app controller execution.**

---

## Architecture Overview

```
App UI (fetch)
  → ASP.NET Core (auth/authz → lookup runner ConnectionId)
    → SignalR: send TunnelRequest { requestId, method, path, body }
      → Runner C# (HubService): receives TunnelRequest
        → HTTP POST to http://localhost:8001{path}
          → FastAPI (uvicorn, multiple workers)
            → Python async handler
          ← JSON response
        ← TunnelResponse { requestId, statusCode, body }
      → SignalR: Runner sends TunnelResponse back to server
    → Server resolves pending TaskCompletionSource by requestId
  ← HTTP 200 to UI
```

**Key properties:**
- Multiple requests share the single WebSocket; each has a unique `requestId`
- Hundreds can be in-flight simultaneously (no serial wait)
- FastAPI workers run in separate processes (real parallelism, no GIL across workers)
- NAT traversal is unchanged — Runner still initiates the outbound WebSocket

---

## Part 1 — Shared Tunnel Protocol Models

**File:** `src/Automaton.Core/Models/Tunnel.cs`  *(new file)*

```csharp
namespace Automaton.Core.Models;

public class TunnelRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = [];
    public string? Body { get; set; }
}

public class TunnelResponse
{
    public string RequestId { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public Dictionary<string, string> Headers { get; set; } = [];
    public string? Body { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Parsed body returned by the FastAPI controller endpoint.
/// Used by ExecuteViaTunnelAsync to avoid reusing the generic OutputVariables string dict.
/// </summary>
public class TunnelControllerResponse
{
    public bool Success { get; set; }
    public System.Text.Json.JsonElement? Data { get; set; }
    public string? Error { get; set; }
}
```

---

## Part 2 — Server: Pending Request Registry

**File:** `src/Automaton.Api/Services/TunnelRequestRegistry.cs`  *(new file)*

Tracks in-flight tunnel requests by `RequestId`. Thread-safe.

```csharp
using System.Collections.Concurrent;
using Automaton.Core.Models;

namespace Automaton.Api.Services;

public class TunnelRequestRegistry
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TunnelResponse>> _pending = new();

    public (string requestId, Task<TunnelResponse> responseTask) Register(TimeSpan timeout, CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<TunnelResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[requestId] = tcs;

        // Auto-cancel on timeout or upstream cancellation
        ct.Register(() => TryFail(requestId, "Request cancelled"));
        _ = Task.Delay(timeout, ct).ContinueWith(_ => TryFail(requestId, "Tunnel request timed out"), ct);

        return (requestId, tcs.Task);
    }

    public bool TryComplete(TunnelResponse response)
    {
        if (_pending.TryRemove(response.RequestId, out var tcs))
        {
            tcs.TrySetResult(response);
            return true;
        }
        return false;
    }

    private void TryFail(string requestId, string reason)
    {
        if (_pending.TryRemove(requestId, out var tcs))
            tcs.TrySetException(new TimeoutException(reason));
    }
}
```

Register as a singleton in `Program.cs`:
```csharp
builder.Services.AddSingleton<TunnelRequestRegistry>();
```

---

## Part 3 — Server: Hub Changes

**File:** `src/Automaton.Api/Hubs/AutomatonHub.cs`

### 3a. Add client method constant

In `AutomatonHubMethods.cs` (or wherever hub method names are defined), add:
```csharp
public const string TunnelRequest  = "TunnelRequest";
public const string TunnelResponse = "TunnelResponse";
```

### 3b. Add `TunnelResponse` server-side hub method

The Runner calls this to deliver a response. The hub resolves the pending TCS.

```csharp
// In AutomatonHub.cs
public async Task TunnelResponse(TunnelResponse response)
{
    var registry = Context.GetHttpContext()!
        .RequestServices.GetRequiredService<TunnelRequestRegistry>();

    if (!registry.TryComplete(response))
    {
        logger.Warning("TunnelResponse for unknown or expired request {RequestId}", response.RequestId);
    }
}
```

---

## Part 4 — Server: Execution Service Changes

**File:** `src/Automaton.Api/Services/AppsControllerExecutionService.cs`

Replace `ExecuteOnRunnersAsync` (the part that calls `InvokeAsync<AppControllerExecutionResponse>(RunAppControllerAction, payload, ...)`) with a tunnel call.

### New private method: `ExecuteViaTunnelAsync`

```csharp
private async Task<RunnerAppControllerResult> ExecuteViaTunnelAsync(
    string connectionId,
    Guid appId,
    string controllerName,
    string actionName,
    Dictionary<string, string> inputs,
    CancellationToken cancellationToken)
{
    var body = JsonSerializer.Serialize(new
    {
        app_id    = appId,
        action    = actionName,
        inputs    = inputs
    });

    var (requestId, responseTask) = tunnelRegistry.Register(
        timeout: TimeSpan.FromSeconds(30),
        ct: cancellationToken);

    var tunnelRequest = new TunnelRequest
    {
        RequestId = requestId,
        Method    = "POST",
        Path      = $"/api/controller/{controllerName}",
        Body      = body
    };

    await automatonHub.Clients
        .Client(connectionId)
        .SendAsync(AutomatonHubMethods.TunnelRequest, tunnelRequest, cancellationToken);

    var tunnelResponse = await responseTask;

    if (tunnelResponse.StatusCode >= 200 && tunnelResponse.StatusCode < 300)
    {
        var payload = JsonSerializer.Deserialize<TunnelControllerResponse>(
            tunnelResponse.Body ?? "{}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return new RunnerAppControllerResult
        {
            Success = payload?.Success ?? false,
            Data    = payload?.Data,
            Error   = payload?.Error
        };
    }

    return new RunnerAppControllerResult
    {
        Success = false,
        Error   = tunnelResponse.Error ?? $"HTTP {tunnelResponse.StatusCode}"
    };
}
```

Inject `TunnelRequestRegistry` via constructor. Replace existing `ExecuteOnRunnersAsync` calls with `ExecuteViaTunnelAsync`. Remove the old `InvokeAsync(RunAppControllerAction, ...)` RPC call entirely.

**Also update `src/Automaton.Api/Models/AppControllerModels.cs`:**
- In `RunnerAppControllerResult`: replace `OutputVariables: Dictionary<string, string>?` with `Data: System.Text.Json.JsonElement?`
- In `AppControllerActionResponse`: replace `OutputVariables: Dictionary<string, string>?` with `Data: System.Text.Json.JsonElement?`
- Remove the `AggregateOutputVariables` private helper (no longer needed; just take `Data` from the first successful result)
- Remove `AppControllerExecutionResponse` class entirely (only existed for the old SignalR RPC path)

---

## Part 5 — Runner: Tunnel Forwarder

**File:** `src/Automaton.Runner/Services/TunnelForwarderService.cs`  *(new file)*

```csharp
using Automaton.Core.Models;
using System.Net.Http;
using System.Text;

namespace Automaton.Runner.Console.Services;

public class TunnelForwarderService
{
    private static readonly HttpClient _client = new()
    {
        BaseAddress = new Uri("http://localhost:8001"),
        Timeout     = TimeSpan.FromSeconds(25)
    };

    // SignalR default max message size is 32KB. Guard before sending to avoid silent failures.
    private const int MaxBodyBytes = 30 * 1024;

    public async Task<TunnelResponse> ForwardAsync(TunnelRequest request)
    {
        var tunnelResponse = new TunnelResponse { RequestId = request.RequestId };

        try
        {
            var httpRequest = new HttpRequestMessage(
                new HttpMethod(request.Method),
                request.Path);

            if (request.Body != null)
                httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");

            foreach (var (k, v) in request.Headers)
                httpRequest.Headers.TryAddWithoutValidation(k, v);

            var httpResponse = await _client.SendAsync(httpRequest);

            tunnelResponse.StatusCode = (int)httpResponse.StatusCode;
            var bodyBytes = await httpResponse.Content.ReadAsByteArrayAsync();

            if (bodyBytes.Length > MaxBodyBytes)
            {
                tunnelResponse.StatusCode = 413;
                tunnelResponse.Error      = $"Response body ({bodyBytes.Length} bytes) exceeds SignalR message limit ({MaxBodyBytes} bytes). Return less data or paginate.";
            }
            else
            {
                tunnelResponse.Body = Encoding.UTF8.GetString(bodyBytes);
            }
        }
        catch (Exception ex)
        {
            tunnelResponse.StatusCode = 502;
            tunnelResponse.Error      = ex.Message;
        }

        return tunnelResponse;
    }
}
```

Register as singleton in `Program.cs`:
```csharp
services.AddSingleton<TunnelForwarderService>();
```

---

## Part 6 — Runner: Hub Handler Changes

**File:** `src/Automaton.Runner/Services/HubService.cs`

### 6a. Add constant
```csharp
private const string TunnelRequestMethod  = "TunnelRequest";
private const string TunnelResponseMethod = "TunnelResponse";
```

### 6b. Register the handler in `RegisterHandlers()` (the method that registers all `connection.On<>` calls)

```csharp
connection.On<TunnelRequest>(TunnelRequestMethod, async (request) =>
{
    var response = await tunnelForwarder.ForwardAsync(request);
    await connection.SendAsync(TunnelResponseMethod, response);
});
```

Inject `TunnelForwarderService` via constructor.

**Also delete from `src/Automaton.Runner/Services/HubService.cs`:**
- Remove the `RunAppControllerActionMethod` and `UploadControllerScriptMethod` constants
- Remove the `connection.On<AppControllerPayload, AppControllerExecutionResponse>(RunAppControllerActionMethod, ...)` handler registration
- Remove the `ExecuteAppControllerActionHandler(AppControllerPayload)` private method
- Remove the `connection.On<...>(UploadControllerScriptMethod, ...)` handler registration

**Also delete `src/Automaton.Runner/Services/AppControllerExecuteService.cs`** entirely — this was the exec()-based Python runner; FastAPI replaces it completely.

---

## Part 7 — FastAPI Server on Runner

**File:** `src/Automaton.Runner/fastapi_server/main.py`  *(new directory + file)*

The FastAPI process runs on port 8001, local-only (not exposed outside the container).

Controllers are plain Python modules that export a `handle(action_name, inputs)` function.
FastAPI loads them via `importlib` — no `exec()`, no injected magic variables.

### Expected controller format (AI-generated)

```python
# backend/tasks.py
import json
import os

DATA_FILE = os.path.join(os.path.dirname(__file__), "tasks.json")

def handle(action_name: str, inputs: dict) -> dict:
    try:
        if action_name == "list":
            tasks = _load()
            return {"success": True, "data": tasks, "error": ""}
        elif action_name == "create":
            title = inputs.get("title", "").strip()
            if not title:
                return {"success": False, "data": None, "error": "title is required"}
            tasks = _load()
            task = {"id": len(tasks) + 1, "title": title, "done": False}
            tasks.append(task)
            _save(tasks)
            return {"success": True, "data": task, "error": ""}
        else:
            return {"success": False, "data": None, "error": f"Unknown action: {action_name}"}
    except Exception as e:
        return {"success": False, "data": None, "error": str(e)}

def _load():
    if not os.path.exists(DATA_FILE):
        return []
    with open(DATA_FILE) as f:
        return json.load(f)

def _save(data):
    with open(DATA_FILE, "w") as f:
        json.dump(data, f)
```

**Return contract**: every `handle()` call returns a `dict` with `success` (bool), `data` (any Python value), `error` (str). FastAPI serializes `data` as standard JSON — no `json.dumps()` needed.

### 7a. Entry point

```python
# main.py
import os
import json
import importlib.util
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
import uvicorn

app = FastAPI(title="Automaton Runner Backend")

SCRIPTS_DIR = os.environ.get("RUNNER_SCRIPTS_DIR", "/app/AppData/controller_scripts")


def load_controller(app_id: str, controller_name: str):
    """Load a controller module from disk by app_id + controller name."""
    base = os.path.join(SCRIPTS_DIR, app_id, "backend")
    script_path = os.path.join(base, controller_name + ".py")

    if not os.path.exists(script_path):
        raise HTTPException(404, f"Controller '{controller_name}' not deployed for app '{app_id}'")

    spec = importlib.util.spec_from_file_location(
        f"{app_id}.{controller_name}", script_path
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)  # reads from disk on every request — hot-reload for free
    return module


@app.post("/api/controller/{controller_name}")
async def execute_controller(controller_name: str, request: Request) -> JSONResponse:
    try:
        body        = await request.json()
        app_id      = body.get("app_id", "")
        action_name = body.get("action", "")
        inputs      = body.get("inputs", {})

        module = load_controller(app_id, controller_name)

        if not hasattr(module, "handle"):
            raise HTTPException(500, f"Controller '{controller_name}' has no handle() function")

        result = module.handle(action_name, inputs)

        return JSONResponse({
            "success": bool(result.get("success", False)),
            "data":    result.get("data"),
            "error":   result.get("error", "")
        })

    except HTTPException:
        raise
    except Exception as e:
        return JSONResponse(
            status_code=500,
            content={"success": False, "data": None, "error": str(e)}
        )


@app.get("/health")
async def health():
    return {"status": "ok"}


if __name__ == "__main__":
    uvicorn.run(
        "main:app",
        host="127.0.0.1",
        port=8001,
        workers=4,        # true parallelism — no GIL across processes
        log_level="warning"
    )
```

### 7b. Requirements file

**File:** `src/Automaton.Runner/fastapi_server/requirements.txt`

```
fastapi>=0.110.0
uvicorn[standard]>=0.29.0
```

---

## Part 8 — Docker Container Changes

**File:** `docker-compose.runner.yml`

The container needs to start both the .NET Runner and the FastAPI server.
Use a shell entrypoint script instead of calling `dotnet` directly.

**File:** `docker/runner/entrypoint.sh`  *(new file)*

```bash
#!/bin/bash
set -e

# Install FastAPI dependencies at container start (packages are pre-baked in the image;
# this is a no-op unless requirements changed, so it's fast)
cd /app/fastapi_server
echo "Installing FastAPI dependencies..."
pip install --quiet --no-cache-dir -r requirements.txt || {
    echo "ERROR: pip install failed — FastAPI backend will not start"
    exit 1
}

# Start FastAPI in background
python main.py &
FASTAPI_PID=$!

# Verify FastAPI started successfully (give it up to 10s)
for i in $(seq 1 10); do
    if curl -sf http://127.0.0.1:8001/health > /dev/null 2>&1; then
        echo "FastAPI backend ready"
        break
    fi
    if [ "$i" -eq 10 ]; then
        echo "ERROR: FastAPI backend did not start within 10s"
        exit 1
    fi
    sleep 1
done

# Start the .NET Runner
cd /app
dotnet automaton-runner.dll

# .NET exited — also stop FastAPI
kill $FASTAPI_PID 2>/dev/null || true
```

> **Note:** This script targets Linux containers (the standard Docker deployment). Windows bare-metal runners do not use this entrypoint — see Open Question #2.

**Dockerfile changes** (add after the .NET publish layer):

```dockerfile
# FastAPI
COPY src/Automaton.Runner/fastapi_server /app/fastapi_server
RUN pip install --no-cache-dir -r /app/fastapi_server/requirements.txt

COPY docker/runner/entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

CMD ["/app/entrypoint.sh"]
```

`RUNNER_SCRIPTS_DIR` environment variable must point to the same path where `AppControllerExecuteService.SaveScript()` writes files. Verify these match; adjust if needed.

---

## Part 9 — Health Check on Runner Startup

**File:** `src/Automaton.Runner/Services/HubService.cs` or startup code

Before connecting to the server, wait for FastAPI to be ready:

```csharp
private async Task WaitForFastApiAsync(int maxWaitSeconds = 30)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    var deadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            var response = await client.GetAsync("http://localhost:8001/health");
            if (response.IsSuccessStatusCode)
            {
                logger.Information("FastAPI backend ready");
                return;
            }
        }
        catch { /* not ready yet */ }
        await Task.Delay(1000);
    }
    logger.Warning("FastAPI backend did not become ready within {Seconds}s — tunnel will be unavailable", maxWaitSeconds);
}
```

Call `WaitForFastApiAsync()` before `ConnectAsync()` in `Program.cs` or `HubService.ConnectAsync()`.

---

## Implementation Order

1. **Part 1** — Add `Tunnel.cs` models to `Automaton.Core`
2. **Part 2** — Add `TunnelRequestRegistry` to server; register in `Program.cs`
3. **Part 3** — Add `TunnelResponse` hub method to `AutomatonHub`; add method name constants
4. **Part 5** — Add `TunnelForwarderService` to Runner; register in `Program.cs`
5. **Part 6** — Add `TunnelRequest` handler to `HubService`
6. **Part 9** — Add `WaitForFastApiAsync` health check to Runner startup
7. **Part 4** — Replace `ExecuteOnRunnersAsync` RPC call with `ExecuteViaTunnelAsync` in `AppsControllerExecutionService`
8. **Part 7** — Create `fastapi_server/main.py` + `requirements.txt`
9. **Part 8** — Update Dockerfile + docker-compose + entrypoint script

---

## Open Questions

1. **Script directory path**: `AppControllerExecuteService.SaveScript()` writes to a local path. Confirm the same path is readable by FastAPI (`RUNNER_SCRIPTS_DIR`). If the paths differ, the find_script logic in `main.py` needs adjustment.

2. **Uvicorn workers vs multiprocessing**: `workers=4` in `uvicorn.run()` uses `fork` on Linux (works in Docker). On Windows bare-metal runners this needs `spawn`. Consider making worker count configurable via env var.

3. **Script hot-reload**: `load_controller()` calls `importlib.util.spec_from_file_location` on every request, reading from disk each time. New deployments are picked up immediately with no FastAPI restart needed.

4. **Response size**: Handled in Part 5 — `TunnelForwarderService` rejects responses over 30 KB with HTTP 413 and a descriptive error. For genuinely large results (file downloads, reports), the app controller should write to a file and return a path or pre-signed URL instead of embedding data in the response.

5. **Authentication inside FastAPI**: The FastAPI server is localhost-only, so it can trust all requests from the Runner C# process. No additional auth needed. Do not expose port 8001 outside the container.

6. **Windows bare-metal runners**: `entrypoint.sh` is Linux-only. Windows runners started with `dotnet run` directly will not have FastAPI managing the subprocess — they need to start FastAPI manually or via a separate `.bat` wrapper. This is a deployment concern, not a code concern; document in the runner setup guide.
