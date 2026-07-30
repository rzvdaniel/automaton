import importlib.util
import io
import json
import os
import sys
from contextlib import redirect_stderr, redirect_stdout


def _load_controller_module(
    scripts_dir: str,
    app_id: str,
    deployment_target_id: str,
    controller_name: str,
):
    backend_dir = os.path.join(
        scripts_dir,
        app_id,
        "targets",
        deployment_target_id,
        "runtime",
        "backend",
    )
    script_path = os.path.join(backend_dir, controller_name + ".py")

    if not os.path.exists(script_path):
        raise FileNotFoundError(
            f"Controller '{controller_name}' not deployed for app '{app_id}'"
        )

    if backend_dir not in sys.path:
        sys.path.insert(0, backend_dir)

    spec = importlib.util.spec_from_file_location(
        f"{app_id}.{controller_name}", script_path
    )
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load controller spec for '{controller_name}'")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main() -> int:
    if len(sys.argv) != 5:
        print(
            json.dumps(
                {
                    "success": False,
                    "data": None,
                    "error": "Usage: controller_runner.py <scripts_dir> <app_id> <deployment_target_id> <controller_name>",
                    "output": None,
                }
            )
        )
        return 1

    scripts_dir, app_id, deployment_target_id, controller_name = sys.argv[1:5]

    try:
        payload = json.loads(sys.stdin.read() or "{}")
        action_name = payload.get("action", "")
        inputs = payload.get("inputs", {})

        module = _load_controller_module(
            scripts_dir, app_id, deployment_target_id, controller_name
        )
        if not hasattr(module, "handle"):
            raise RuntimeError(f"Controller '{controller_name}' has no handle() function")

        buf = io.StringIO()
        with redirect_stdout(buf), redirect_stderr(buf):
            result = module.handle(action_name, inputs)
        output = buf.getvalue() or None

        if not isinstance(result, dict):
            raise RuntimeError(
                f"Controller '{controller_name}' returned {type(result).__name__}, expected dict"
            )

        response = {
            "success": bool(result.get("success", False)),
            "data": result.get("data"),
            "error": result.get("error", ""),
            "output": output,
        }

        json.dumps(response)
        print(json.dumps(response))
        return 0
    except Exception as ex:
        print(
            json.dumps(
                {"success": False, "data": None, "error": str(ex), "output": None}
            )
        )
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
