import base64
import json
import mimetypes
import os
import subprocess
import sys
import shutil
import tempfile
import uuid
from fastapi import FastAPI, File, Form, HTTPException, Request, UploadFile
from fastapi.responses import FileResponse, JSONResponse
import uvicorn

app = FastAPI(title="Automaton Runner Backend")


@app.get("/shutdown")
async def shutdown():
    """Graceful shutdown endpoint — called by the runner before relaunching with fresh credentials."""
    import threading

    threading.Timer(0.1, lambda: os._exit(0)).start()
    return {"status": "shutting down"}


def _default_scripts_dir() -> str:
    return os.path.join(_default_workspace_root(), "apps")


def _default_workspace_root() -> str:
    if (
        os.environ.get("DOTNET_RUNNING_IN_CONTAINER", "").lower() == "true"
        or os.path.exists("/.dockerenv")
    ):
        return "/automaton/workspace"

    runner_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    return os.path.join(runner_root, "workspace")


def _workspace_root_from_env() -> str | None:
    value = os.environ.get("AUTOMATON_WORKSPACE_ROOT")
    return value or None


def _effective_workspace_root() -> str:
    return _workspace_root_from_env() or _default_workspace_root()


SCRIPTS_DIR = os.environ.get("RUNNER_SCRIPTS_DIR") or _default_scripts_dir()
WORKSPACE_ROOT = _effective_workspace_root()
RUNNER_SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "controller_runner.py")
RUNNER_DLL = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "automaton-runner.dll"))
DOTNET_HOST = shutil.which("dotnet") or "dotnet"
FASTAPI_HOST = os.environ.get("RUNNER_FASTAPI_HOST", "127.0.0.1").strip() or "127.0.0.1"


def _print_startup_diagnostics() -> None:
    env_value = os.environ.get("RUNNER_SCRIPTS_DIR")
    scripts_dir_abs = os.path.abspath(SCRIPTS_DIR)
    cwd_abs = os.path.abspath(os.getcwd())
    source_tree_hint = os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "workspace", "apps")
    )

    print(f"[Automaton FastAPI] RUNNER_SCRIPTS_DIR resolved to: {scripts_dir_abs}")
    print(f"[Automaton FastAPI] AUTOMATON_WORKSPACE_ROOT resolved to: {os.path.abspath(WORKSPACE_ROOT)}")
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
            "If your runner deployed files under bin/.../workspace/apps, this instance will not see them. "
            "Start the runner instead, or set RUNNER_SCRIPTS_DIR explicitly before launching FastAPI."
        )


@app.get("/debug-scripts-dir")
async def debug_scripts_dir():
    return {
        "scripts_dir": SCRIPTS_DIR,
        "runner_scripts_dir_env": os.environ.get("RUNNER_SCRIPTS_DIR", "(not set)"),
        "workspace_root": WORKSPACE_ROOT,
    }


def _venv_python_path(app_id: str, deployment_target_id: str) -> str:
    target_home = os.path.join(
        SCRIPTS_DIR, app_id, "targets", deployment_target_id
    )
    if sys.platform == "win32":
        return os.path.join(target_home, "venv", "Scripts", "python.exe")
    return os.path.join(target_home, "venv", "bin", "python")


def _app_data_dir(app_home: str, app_id: str) -> str:
    if _workspace_root_from_env() is None and os.environ.get("RUNNER_SCRIPTS_DIR"):
        return os.path.join(app_home, "data")

    return os.path.join(WORKSPACE_ROOT, "data", app_id)


def _execute_in_app_env(
    app_id: str,
    deployment_target_id: str,
    controller_name: str,
    action_name: str,
    inputs: dict,
):
    target_home, backend_dir, data_dir = _resolve_app_paths(
        app_id, deployment_target_id
    )
    python_path = _venv_python_path(app_id, deployment_target_id)
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
            "AUTOMATON_DEPLOYMENT_TARGET_ID": deployment_target_id,
            "AUTOMATON_APP_HOME": target_home,
            "AUTOMATON_APP_BACKEND_DIR": backend_dir,
            "AUTOMATON_APP_DATA_DIR": data_dir,
        }
    )

    process = subprocess.run(
        [
            python_path,
            RUNNER_SCRIPT,
            SCRIPTS_DIR,
            app_id,
            deployment_target_id,
            controller_name,
        ],
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


def _resolve_app_paths(
    app_id: str, deployment_target_id: str
) -> tuple[str, str, str]:
    if not app_id:
        raise HTTPException(status_code=400, detail="app_id is required")
    if not deployment_target_id:
        raise HTTPException(
            status_code=400, detail="deployment_target_id is required"
        )
    try:
        uuid.UUID(app_id)
        uuid.UUID(deployment_target_id)
    except ValueError as error:
        raise HTTPException(
            status_code=400, detail="app and deployment target ids must be UUIDs"
        ) from error

    app_home = os.path.abspath(os.path.join(SCRIPTS_DIR, app_id))
    scripts_root = os.path.abspath(SCRIPTS_DIR)
    if not app_home.startswith(scripts_root + os.sep):
        raise HTTPException(status_code=400, detail="invalid app id")

    target_home = os.path.abspath(
        os.path.join(app_home, "targets", deployment_target_id)
    )
    if not target_home.startswith(app_home + os.sep):
        raise HTTPException(status_code=400, detail="invalid deployment target id")

    backend_dir = os.path.join(target_home, "runtime", "backend")
    data_dir = _app_data_dir(app_home, app_id)

    if not os.path.isdir(target_home):
        raise HTTPException(
            status_code=404,
            detail=f"unknown deployment target '{deployment_target_id}' for app '{app_id}'",
        )

    return target_home, backend_dir, data_dir


def _temp_upload_dir(session_id: str) -> str:
    return os.path.join(tempfile.gettempdir(), "uploads", session_id)


def _temp_results_dir(session_id: str) -> str:
    return os.path.join(tempfile.gettempdir(), "results", session_id)


def _safe_download_file(session_id: str) -> str | None:
    results_dir = _temp_results_dir(session_id)
    if not os.path.isdir(results_dir):
        return None

    for entry in os.scandir(results_dir):
        if entry.is_file():
            return entry.path

    return None


@app.post("/api/controller/{controller_name}")
async def execute_controller(controller_name: str, request: Request) -> JSONResponse:
    try:
        body = await request.json()
        app_id = str(body.get("app_id", ""))
        deployment_target_id = str(body.get("deployment_target_id", ""))
        action_name = body.get("action", "")
        inputs = body.get("inputs", {})

        result = _execute_in_app_env(
            app_id,
            deployment_target_id,
            controller_name,
            action_name,
            inputs,
        )

        return JSONResponse(status_code=200, content=result)
    except HTTPException:
        raise
    except Exception as e:
        return JSONResponse(
            status_code=500,
            content={"success": False, "data": None, "error": str(e)},
        )


@app.post("/api/runtime-file")
async def read_runtime_file(request: Request) -> JSONResponse:
    try:
        body = await request.json()
        app_id = str(body.get("app_id", ""))
        deployment_target_id = str(body.get("deployment_target_id", ""))
        relative_path = str(body.get("path", "index.html")).strip().lstrip("/\\")

        if not app_id:
            return JSONResponse(status_code=400, content={"error": "app_id is required"})

        target_home, _, _ = _resolve_app_paths(app_id, deployment_target_id)
        frontend_dir = os.path.join(target_home, "runtime", "frontend")
        full_path = os.path.abspath(os.path.join(frontend_dir, relative_path))

        if not full_path.startswith(os.path.abspath(frontend_dir) + os.sep) and full_path != os.path.abspath(frontend_dir):
            return JSONResponse(status_code=400, content={"error": "invalid path"})

        if not os.path.isfile(full_path):
            return JSONResponse(status_code=404, content={"error": "file not found"})

        with open(full_path, "rb") as f:
            content = f.read()

        content_type, _ = mimetypes.guess_type(full_path)
        if not content_type:
            content_type = "application/octet-stream"

        return JSONResponse(
            status_code=200,
            content={
                "contentBase64": base64.b64encode(content).decode("ascii"),
                "contentType": content_type,
            },
        )
    except Exception as e:
        return JSONResponse(status_code=500, content={"error": str(e)})


@app.post("/internal/apps/{app_id}/targets/{deployment_target_id}/api/controllers/{controller_name}/actions/{action_name}/execute")
async def execute_internal_controller(
    app_id: str,
    deployment_target_id: str,
    controller_name: str,
    action_name: str,
    request: Request,
) -> JSONResponse:
    try:
        _resolve_app_paths(app_id, deployment_target_id)
        body = await request.json()
        inputs = body.get("inputs", {}) if isinstance(body, dict) else {}
        result = _execute_in_app_env(
            app_id,
            deployment_target_id,
            controller_name,
            action_name,
            inputs,
        )
        return JSONResponse(status_code=200, content=result)
    except HTTPException as ex:
        return JSONResponse(status_code=ex.status_code, content={"success": False, "data": None, "error": ex.detail})
    except Exception as ex:
        return JSONResponse(status_code=500, content={"success": False, "data": None, "error": str(ex)})


@app.post("/internal/apps/{app_id}/targets/{deployment_target_id}/api/flows/{flow_identifier}/execute")
async def execute_internal_flow(
    app_id: str,
    deployment_target_id: str,
    flow_identifier: str,
    request: Request,
) -> JSONResponse:
    try:
        _resolve_app_paths(app_id, deployment_target_id)
        body = await request.json() if request.headers.get("content-type", "").startswith("application/json") else {}
        inputs = body.get("inputs", {}) if isinstance(body, dict) else {}
        inputs_json = json.dumps(inputs)

        process = subprocess.run(
            [
                DOTNET_HOST,
                RUNNER_DLL,
                "execute-app-flow",
                "--app-id",
                app_id,
                "--deployment-target-id",
                deployment_target_id,
                "--flow-identifier",
                flow_identifier,
                "--inputs-json",
                inputs_json,
            ],
            text=True,
            capture_output=True,
            check=False,
            cwd=os.path.dirname(RUNNER_DLL),
        )

        stdout = (process.stdout or "").strip()
        if stdout:
            try:
                payload = json.loads(stdout)
            except json.JSONDecodeError:
                payload = {
                    "success": False,
                    "status": "Error",
                    "runnerCount": 1,
                    "executedAt": None,
                    "message": stdout,
                    "outputVariables": {},
                    "logs": [],
                }
        else:
            payload = {
                "success": False,
                "status": "Error",
                "runnerCount": 1,
                "executedAt": None,
                "message": (process.stderr or "Flow execution returned no payload.").strip(),
                "outputVariables": {},
                "logs": [],
            }

        status_code = 200 if process.returncode == 0 else (404 if "not found" in str(payload.get("message", "")).lower() else 400)
        return JSONResponse(status_code=status_code, content=payload)
    except HTTPException as ex:
        return JSONResponse(status_code=ex.status_code, content={"message": ex.detail})


@app.post("/internal/apps/{app_id}/targets/{deployment_target_id}/api/files/upload")
async def upload_internal_file(
    app_id: str,
    deployment_target_id: str,
    file: UploadFile = File(...),
    runnerNames: str | None = Form(default=None),
) -> JSONResponse:
    del runnerNames

    try:
        _resolve_app_paths(app_id, deployment_target_id)
        session_id = uuid.uuid4().hex
        target_dir = _temp_upload_dir(session_id)
        os.makedirs(target_dir, exist_ok=True)

        safe_name = os.path.basename(file.filename or "upload.bin")
        target_path = os.path.join(target_dir, safe_name)

        with open(target_path, "wb") as target:
            while True:
                chunk = await file.read(1024 * 1024)
                if not chunk:
                    break
                target.write(chunk)

        await file.close()
        return JSONResponse(status_code=200, content={"session_id": session_id})
    except HTTPException as ex:
        return JSONResponse(status_code=ex.status_code, content={"error": ex.detail})
    except Exception as ex:
        return JSONResponse(status_code=500, content={"error": str(ex)})


@app.get("/internal/apps/{app_id}/targets/{deployment_target_id}/api/files/{session_id}")
async def download_internal_file(
    app_id: str, deployment_target_id: str, session_id: str
):
    try:
        _resolve_app_paths(app_id, deployment_target_id)
        file_path = _safe_download_file(session_id)
        if not file_path:
            return JSONResponse(status_code=404, content={"error": "file not found"})

        file_name = os.path.basename(file_path)
        content_type, _ = mimetypes.guess_type(file_path)
        return FileResponse(
            file_path,
            media_type=content_type or "application/octet-stream",
            filename=file_name,
        )
    except HTTPException as ex:
        return JSONResponse(status_code=ex.status_code, content={"error": ex.detail})


@app.api_route("/internal/apps/{app_id}/targets/{deployment_target_id}/api/{path:path}", methods=["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD"])
async def unknown_internal_api(
    app_id: str, deployment_target_id: str, path: str
) -> JSONResponse:
    try:
        _resolve_app_paths(app_id, deployment_target_id)
    except HTTPException as ex:
        return JSONResponse(status_code=ex.status_code, content={"error": ex.detail})

    return JSONResponse(status_code=404, content={"error": f"unsupported managed runtime backend path '{path}'"})


@app.get("/health")
async def health():
    return {"status": "ok"}


if __name__ == "__main__":
    _print_startup_diagnostics()
    try:
        configured_workers = int(os.environ.get("RUNNER_FASTAPI_WORKERS", "1"))
    except ValueError:
        configured_workers = 1
    workers = 1 if sys.platform == "win32" else max(1, configured_workers)
    uvicorn.run(
        "main:app", host=FASTAPI_HOST, port=8001, workers=workers, log_level="info"
    )
