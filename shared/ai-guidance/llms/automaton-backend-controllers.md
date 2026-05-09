# Automaton Backend Controllers

Automaton backends are Python controllers stored in `backend/`.

Rules:
- Each controller is one Python file.
- The controller name is the filename without `.py`.
- Each controller file must export `handle(action_name, inputs)`.
- Responses must use the envelope `{ success, data, error }`.
- Shared helpers go in `backend/shared/`.
- Extra Python packages go in `backend/requirements.txt`.
- Local mock responses go in `backend/mocks/{controller}.{action}.json`.

Recommended pattern:

```python
class TodosController:
    def list(self, inputs: dict) -> dict:
        return {"success": True, "data": {"items": []}, "error": ""}

    def create(self, inputs: dict) -> dict:
        title = inputs.get("title", "").strip()
        if not title:
            return {"success": False, "data": None, "error": "title is required"}
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
