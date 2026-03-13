import os
import sys
import importlib.util
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
import uvicorn

app = FastAPI(title="Automaton Runner Backend")

# RUNNER_SCRIPTS_DIR must point to the root of the app data directory.
# FastAPI resolves controller scripts at: {RUNNER_SCRIPTS_DIR}/{app_id}/backend/{controller_name}.py
# Per-app pip packages live at:           {RUNNER_SCRIPTS_DIR}/{app_id}/packages/
# This must match the path where .NET writes scripts:
#   AppContext.BaseDirectory/data/apps/{appId}/backend/{controllerName}.py
SCRIPTS_DIR = os.environ.get("RUNNER_SCRIPTS_DIR", "/app/data/apps")


def _inject_packages(app_id: str) -> None:
    """Prepend the per-app packages directory to sys.path so pip-installed packages are importable."""
    packages_dir = os.path.join(SCRIPTS_DIR, app_id, "packages")
    if os.path.isdir(packages_dir) and packages_dir not in sys.path:
        sys.path.insert(0, packages_dir)


def load_controller(app_id: str, controller_name: str):
    """Load a controller module from disk by app_id + controller name."""
    _inject_packages(app_id)

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
        body = await request.json()
        app_id = body.get("app_id", "")
        action_name = body.get("action", "")
        inputs = body.get("inputs", {})

        module = load_controller(str(app_id), controller_name)

        if not hasattr(module, "handle"):
            raise HTTPException(500, f"Controller '{controller_name}' has no handle() function")

        result = module.handle(action_name, inputs)

        return JSONResponse({
            "success": bool(result.get("success", False)),
            "data": result.get("data"),
            "error": result.get("error", "")
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
    # Windows does not support uvicorn multi-worker mode (no fork/socket passing).
    # On Linux (Docker), use 4 workers for true parallelism across GIL boundaries.
    import sys
    workers = 1 if sys.platform == "win32" else 4
    uvicorn.run(
        "main:app",
        host="127.0.0.1",
        port=8001,
        workers=workers,
        log_level="info"
    )
