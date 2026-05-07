import contextlib
import importlib.util
import io
import json
import os
import sys
import traceback
import uuid


FAILURE_IMPORT_ERROR = "import_error"
FAILURE_MISSING_HANDLE = "missing_handle"
FAILURE_RUNTIME_EXCEPTION = "runtime_exception"
FAILURE_MISSING_DEPENDENCY = "missing_dependency"
FAILURE_INVALID_INPUT = "invalid_input"


def emit(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload))
    sys.stdout.flush()


def failure(error_type: str, error: str, tb: str | None = None, stdout: str | None = None, stderr: str | None = None) -> None:
    emit(
        {
            "ok": False,
            "errorType": error_type,
            "error": error,
            "traceback": tb,
            "stdout": stdout,
            "stderr": stderr,
        }
    )


def load_request() -> dict:
    try:
        raw = sys.stdin.read()
        return json.loads(raw or "{}")
    except Exception as exc:  # pragma: no cover - defensive path
        failure(FAILURE_INVALID_INPUT, f"Invalid request payload: {exc}", traceback.format_exc())
        raise SystemExit(1)


def load_module(project_root: str, controller_name: str, controller_path: str):
    backend_dir = os.path.join(project_root, "backend")
    if backend_dir not in sys.path:
        sys.path.insert(0, backend_dir)

    unique_name = f"automaton_local_{controller_name}_{uuid.uuid4().hex}"
    spec = importlib.util.spec_from_file_location(unique_name, controller_path)
    if spec is None or spec.loader is None:
        raise ImportError(f"Could not load controller '{controller_name}' from {controller_path}")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


request = load_request()
project_root = request.get("projectRoot") or os.getcwd()
controller_name = request.get("controllerName") or "controller"
controller_path = request.get("controllerPath")
operation = request.get("operation") or "invoke"

if not controller_path:
    failure(FAILURE_INVALID_INPUT, "controllerPath is required.")
    raise SystemExit(1)

os.chdir(project_root)

try:
    module = load_module(project_root, controller_name, controller_path)
except ModuleNotFoundError as exc:
    failure(FAILURE_MISSING_DEPENDENCY, str(exc), traceback.format_exc())
    raise SystemExit(0)
except Exception as exc:
    failure(FAILURE_IMPORT_ERROR, str(exc), traceback.format_exc())
    raise SystemExit(0)

if not hasattr(module, "handle") or not callable(module.handle):
    failure(FAILURE_MISSING_HANDLE, f"Controller '{controller_name}' does not export handle(action_name, inputs).")
    raise SystemExit(0)

if operation == "inspect":
    emit({"ok": True})
    raise SystemExit(0)

stdout_buffer = io.StringIO()
stderr_buffer = io.StringIO()

try:
    with contextlib.redirect_stdout(stdout_buffer), contextlib.redirect_stderr(stderr_buffer):
        response = module.handle(request.get("actionName") or "", request.get("inputs") or {})
except Exception as exc:
    failure(
        FAILURE_RUNTIME_EXCEPTION,
        str(exc),
        traceback.format_exc(),
        stdout_buffer.getvalue() or None,
        stderr_buffer.getvalue() or None,
    )
    raise SystemExit(0)

emit(
    {
        "ok": True,
        "response": response,
        "stdout": stdout_buffer.getvalue() or None,
        "stderr": stderr_buffer.getvalue() or None,
    }
)
