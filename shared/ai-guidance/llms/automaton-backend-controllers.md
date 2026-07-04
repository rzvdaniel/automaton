---
title: "Automaton Backend Controllers"
description: "Python controller rules, response envelope, and backend structure guidance."
---

# Automaton Backend Controllers

Automaton backends are Python controllers stored in `backend/`.

Rules:
- CRITICAL: Automaton backends are not standalone HTTP servers.
- Default to frontend-only. Create `backend/` only when the user-requested core feature requires server-side execution, secrets, persistence, auth, scheduled work, or browser-unsafe access that cannot be removed without breaking the requested app.
- Never create FastAPI, Flask, Express, aiohttp, uvicorn entrypoints, or custom HTTP listeners under `backend/`.
- Do not create `backend/` only for convenience, caching, minor proxying, CORS or mixed-content workarounds for non-essential data, or optional enrichment.
- Mixed-content or CORS issues for optional third-party APIs are not sufficient justification for introducing a backend web framework or proxy.
- If you think a proxy or API server is needed, first attempt a frontend-only solution. If backend behavior is truly required, implement an Automaton controller instead.
- Each controller is one Python file.
- The controller name is the filename without `.py`.
- Each controller file must export `handle(action_name, inputs)`.
- Responses must use the envelope `{ success, data, error }`.
- Shared helpers go in `backend/shared/`.
- Extra Python packages go in `backend/requirements.txt`.
- Local mock responses go in `backend/mocks/{controller}.{action}.json`.
- For Python diagnostics, prefer the standard `logging` module over `print(...)`.
- Use a module-level logger with `logger.info(...)`, `logger.warning(...)`, and `logger.exception(...)` for meaningful backend runtime messages.
- `print(...)` is still compatible because Automaton captures stdout, but it should not be the default recommendation in generated apps.

Recommended pattern:

```python
import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger("todos")

class TodosController:
    def list(self, inputs: dict) -> dict:
        logger.info("Listing todos")
        return {"success": True, "data": {"items": []}, "error": ""}

    def create(self, inputs: dict) -> dict:
        title = inputs.get("title", "").strip()
        if not title:
            logger.warning("Todo creation rejected because title is missing")
            return {"success": False, "data": None, "error": "title is required"}
        logger.info("Creating todo")
        return {"success": True, "data": {"title": title}, "error": ""}

_controller = TodosController()

def handle(action_name: str, inputs: dict) -> dict:
    method = getattr(_controller, action_name, None)
    if method is None:
        return {"success": False, "data": None, "error": f"Unknown action: {action_name}"}
    return method(inputs)
```

Do not flatten `data` in `handle()`.
Do not return bare objects without `success`, `data`, and `error`.
