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
    main.py
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

`backend/main.py`

```python
from fastapi import FastAPI

app = FastAPI()

@app.get("/api/todos")
def list_todos():
    return {"items": []}
```

`frontend/src/api.ts`

```ts
export async function listTodos() {
  const response = await fetch('/api/todos')
  if (!response.ok) throw new Error(`Request failed: ${response.status}`)
  return response.json()
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
