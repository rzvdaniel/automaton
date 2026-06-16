import json
import os
import subprocess
import sys
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
import uvicorn

app = FastAPI(title="Automaton Runner Backend")


@app.get("/shutdown")
async def shutdown():
    """Graceful shutdown endpoint — called by the runner before relaunching with fresh credentials."""
    import threading

    threading.Timer(0.1, lambda: os._exit(0)).start()
    return {"status": "shutting down"}


def _default_scripts_dir() -> str:
    if (
        os.environ.get("DOTNET_RUNNING_IN_CONTAINER", "").lower() == "true"
        or os.path.exists("/.dockerenv")
    ):
        return "/app/data/apps"

    runner_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(runner_root, "data", "apps")


SCRIPTS_DIR = os.environ.get("RUNNER_SCRIPTS_DIR") or _default_scripts_dir()
RUNNER_SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "controller_runner.py")


def _print_startup_diagnostics() -> None:
    env_value = os.environ.get("RUNNER_SCRIPTS_DIR")
    scripts_dir_abs = os.path.abspath(SCRIPTS_DIR)
    cwd_abs = os.path.abspath(os.getcwd())
    source_tree_hint = os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "data", "apps")
    )

    print(f"[Automaton FastAPI] RUNNER_SCRIPTS_DIR resolved to: {scripts_dir_abs}")
    print(f"[Automaton FastAPI] Working directory: {cwd_abs}")
    print(
        "[Automaton FastAPI] RUNNER_SCRIPTS_DIR source: "
        + ("environment variable" if env_value else "fallback path")
    )

    if not os.path.isdir(scripts_dir_abs):
        print(
            "[Automaton FastAPI] WARNING: scripts directory does not exist yet: "
            f"{scripts_dir_abs}"
        )

    if not env_value and scripts_dir_abs == source_tree_hint:
        print(
            "[Automaton FastAPI] WARNING: FastAPI appears to be running manually from the source tree. "
            "If your runner deployed files under bin/.../data/apps, this instance will not see them. "
            "Start the runner instead, or set RUNNER_SCRIPTS_DIR explicitly before launching FastAPI."
        )


@app.get("/debug-scripts-dir")
async def debug_scripts_dir():
    return {
        "scripts_dir": SCRIPTS_DIR,
        "runner_scripts_dir_env": os.environ.get("RUNNER_SCRIPTS_DIR", "(not set)"),
    }


def _venv_python_path(app_id: str) -> str:
    if sys.platform == "win32":
        return os.path.join(SCRIPTS_DIR, app_id, "venv", "Scripts", "python.exe")
    return os.path.join(SCRIPTS_DIR, app_id, "venv", "bin", "python")


def _execute_in_app_env(app_id: str, controller_name: str, action_name: str, inputs: dict):
    app_home = os.path.abspath(os.path.join(SCRIPTS_DIR, app_id))
    backend_dir = os.path.join(app_home, "backend")
    data_dir = os.path.join(app_home, "data")
    python_path = _venv_python_path(app_id)
    if not os.path.exists(python_path):
        raise HTTPException(
            500,
            f"App virtual environment not found for app '{app_id}'. Deploy the backend again to create it.",
        )

    if not os.path.isdir(backend_dir):
        raise HTTPException(
            500,
            f"App backend directory not found for app '{app_id}'. Deploy the backend again.",
        )

    os.makedirs(data_dir, exist_ok=True)

    payload = json.dumps({"action": action_name, "inputs": inputs})
    env = os.environ.copy()
    env.update(
        {
            "AUTOMATON_APP_ID": app_id,
            "AUTOMATON_APP_HOME": app_home,
            "AUTOMATON_APP_BACKEND_DIR": backend_dir,
            "AUTOMATON_APP_DATA_DIR": data_dir,
        }
    )

    process = subprocess.run(
        [python_path, RUNNER_SCRIPT, SCRIPTS_DIR, app_id, controller_name],
        input=payload,
        text=True,
        capture_output=True,
        check=False,
        cwd=backend_dir,
        env=env,
    )

    stdout = process.stdout.strip()
    stderr = process.stderr.strip()

    if process.returncode != 0:
        detail = stderr or stdout or f"controller runner exit code {process.returncode}"
        return {
            "success": False,
            "data": None,
            "error": f"Controller process failed: {detail}",
            "output": None,
        }

    if not stdout:
        return {
            "success": False,
            "data": None,
            "error": "Controller process returned no JSON payload.",
            "output": stderr or None,
        }

    try:
        return json.loads(stdout)
    except json.JSONDecodeError as ex:
        detail = stderr or stdout
        return {
            "success": False,
            "data": None,
            "error": f"Controller returned invalid JSON: {ex}",
            "output": detail or None,
        }


@app.post("/api/controller/{controller_name}")
async def execute_controller(controller_name: str, request: Request) -> JSONResponse:
    try:
        body = await request.json()
        app_id = str(body.get("app_id", ""))
        action_name = body.get("action", "")
        inputs = body.get("inputs", {})

        result = _execute_in_app_env(app_id, controller_name, action_name, inputs)

        return JSONResponse(status_code=200, content=result)
    except HTTPException:
        raise
    except Exception as e:
        return JSONResponse(
            status_code=500,
            content={"success": False, "data": None, "error": str(e)},
        )


@app.get("/health")
async def health():
    return {"status": "ok"}


if __name__ == "__main__":
    _print_startup_diagnostics()
    workers = 1 if sys.platform == "win32" else 4
    uvicorn.run(
        "main:app", host="127.0.0.1", port=8001, workers=workers, log_level="info"
    )
