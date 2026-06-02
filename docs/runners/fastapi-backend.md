# Runner FastAPI Backend

The runner includes a lightweight FastAPI server (port 8001) that executes Python app controller scripts. The .NET runner communicates with it over localhost via the HTTP tunnel architecture — no external exposure.

## Architecture

```
Browser / App
    |
    v
ASP.NET Server  --[SignalR TunnelRequest]--> Runner (C#)
                                                 |
                                         TunnelForwarderService
                                                 |
                                         http://localhost:8001
                                                 |
                                           FastAPI (Python)
                                                 |
                                 launches app-specific venv subprocess
                                                 |
                      {RUNNER_SCRIPTS_DIR}/{appId}/venv/bin/python (or Scripts/python.exe)
                                                 |
                                     loads {RUNNER_SCRIPTS_DIR}/{appId}/backend/{controller}.py
                                     calls handle(action, inputs) -> {success, data, error}
```

## Script Storage

Controller scripts are stored at:

```
{RUNNER_SCRIPTS_DIR}/{appId}/backend/{controllerName}.py
```

The path is configured via the `RUNNER_SCRIPTS_DIR` environment variable:

| Environment | Default path |
|---|---|
| Docker | `/app/data/apps` (named volume `runner-data`) |
| Local dev | `<worktree>/runner-data/apps` (set by `dev-start.ps1`) |

Both the C# runner (`HubService.UploadControllerScriptHandler`) and FastAPI read from this same path.

Each app also gets its own virtual environment:

```
{RUNNER_SCRIPTS_DIR}/{appId}/venv
```

FastAPI stays as the shared request router, but controller code executes in a subprocess using that app's venv interpreter. This isolates app-specific Python dependencies without needing one FastAPI server per app.

## Controller Script Contract

Each deployed script must expose a `handle` function:

```python
def handle(action_name: str, inputs: dict) -> dict:
    # action_name: the action called from the app
    # inputs: dict of string key/value pairs from the app
    return {
        "success": True,         # bool
        "data": { ... },         # any JSON-serialisable value
        "error": ""              # error message string, empty on success
    }
```

## Running Locally (dev-start.ps1)

The `scripts/dev-start.ps1` script handles everything automatically:

1. Checks Python is installed
2. Checks `fastapi` and `uvicorn` are installed — warns if missing
3. Sets `RUNNER_SCRIPTS_DIR` in the current process (inherited by child windows)
4. Opens a dedicated FastAPI terminal window
5. Opens the .NET runner window (shares the same scripts directory)

**One-time setup:**
```powershell
pip install fastapi "uvicorn[standard]"
```

After that, just run:
```powershell
.\scripts\dev-start.ps1
```

## Running in Docker

FastAPI is started automatically by `docker-entrypoint.sh` before the .NET runner connects:

```bash
docker compose -f docker-compose.runner.yml up -d --build
```

The `runner-data` named volume persists deployed scripts across container restarts.

> **Note:** If you see `Access to the path '/app/data/apps' is denied`, the volume was created before the ownership fix. Delete and recreate it:
> ```bash
> docker compose -f docker-compose.runner.yml down
> docker volume rm <project>_runner-data
> docker compose -f docker-compose.runner.yml up -d --build
> ```

## Workers

| Platform | Workers | Reason |
|---|---|---|
| Linux / Docker | 4 | Multi-process parallelism, bypasses GIL |
| Windows (local dev) | 1 | Windows does not support uvicorn multi-process socket passing |

This is detected automatically at startup in `main.py` — no configuration needed.

## Health Check

FastAPI exposes a health endpoint the .NET runner polls before connecting to SignalR:

```
GET http://localhost:8001/health
-> {"status": "ok"}
```

The runner waits up to 30 seconds for FastAPI to become ready. If it does not respond in time, a warning is printed to the console and the runner connects anyway (Deploy Backend works, but tunnel execution will fail until FastAPI is available).

## Logging

- **Server side**: `AppsControllerExecutionService` logs tunnel requests/responses to Serilog (visible in Admin Logs)
- **Runner side**: `HubService` and `TunnelForwarderService` log tunnel activity to Serilog
- **App execution logs**: Every controller action (public and preview) is recorded in `AppExecutionLog` (visible in App Edit > Logs)
