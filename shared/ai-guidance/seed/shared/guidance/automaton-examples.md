# Automaton Examples

Use these examples when an agent needs concrete starter files instead of prose.

## Full-stack todo file tree

```text
todo-app/
  automaton.json
  .automatonignore
  frontend/
    package.json
    src/
      api.ts
      main.ts
  backend/
    todos.py
```

## Minimal frontend `automaton.json`

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "My App",
  "server": "https://api.automaton.monster",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  }
}
```

## Full-stack todo starter

`automaton.json`

```json
{
  "appId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "Todo App",
  "server": "https://api.automaton.monster",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "deployDir": "backend"
  }
}
```

`backend/todos.py`

```python
class TodosController:
    def list(self, inputs: dict) -> dict:
        return {"success": True, "data": {"items": []}, "error": ""}

_controller = TodosController()

def handle(action_name: str, inputs: dict) -> dict:
    method = getattr(_controller, action_name, None)
    if method is None:
        return {"success": False, "data": None, "error": f"Unknown action: {action_name}"}

    return method(inputs)
```

`frontend/src/api.ts`

```ts
export async function listTodos() {
  return window.automatonApi.callController('todos', 'list', {})
}
```

If the project is scaffolded locally but not linked yet, the temporary local `automaton.json` can omit `appId`:

```json
{
  "name": "Todo App",
  "server": "https://api.automaton.monster",
  "frontend": {
    "build": "cd frontend && npm install && npm run build",
    "deployDir": "frontend/dist"
  },
  "backend": {
    "deployDir": "backend"
  }
}
```

After remote creation succeeds, update that file to include `appId`.
