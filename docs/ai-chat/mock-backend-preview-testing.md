# Mock Backend in Preview Testing

## What this enables

Preview mode can now execute app controller actions against **mock responses** instead of real runner-backed Python controllers.

This is useful for AI-driven UI testing where you want deterministic behavior and no persistence side effects.

---

## Default behavior

In preview, mock backend is enabled by default.

- Build Visual preview: uses mock mode
- Open Preview dialog: uses the same preview injection pipeline, so it also uses mock mode

Both views share the same `window.automatonApi` runtime injected into preview HTML.

---

## How routing works

When preview calls:

- `window.automatonApi.callController(controller, action, inputs)`

the request body includes:

- `useMockBackend: true` (default in preview)

Backend controller execution checks this flag:

- if `true`, it attempts to load a mock file
- if mock file exists, returns mock response immediately (no runner call)
- if mock file is missing, falls back to real runner execution

---

## Mock file convention

Store mock files in app files under:

- `backend/mocks/{controllerName}.{actionName}.json`

Example:

- `backend/mocks/todos.addTodo.json`

Example content:

```json
{
  "success": true,
  "status": "Completed",
  "data": {
    "id": "todo-123",
    "title": "Buy groceries",
    "completed": false
  }
}
```

Optional fields:

- `success` (defaults to `true` if omitted)
- `status` (defaults to `Completed`/`Failed`)
- `data`
- `error`

---

## AI generation guidance

Yes — you should instruct AI to generate mocks alongside controller scripts.

Recommended prompt rule for app generation:

1. For every backend controller action, also create a matching mock JSON file in `backend/mocks/`.
2. Ensure mock `data` matches the action response schema used by frontend code.
3. Include both success and failure variants when the UI has error handling branches.

This keeps testing stable while preserving real backend scripts for later deployment.

---

## Runtime controls

Preview runtime now exposes:

- `window.automatonApi.getBackendMode()` → `"mock"` or `"real"`
- `window.automatonApi.setBackendMode('mock' | 'real')`

So AI (or user scripts) can temporarily switch to real backend if needed.

---

## Related docs

- `docs/ai-chat/preview-test-run-isolation.md`
- `docs/ai-chat/autonomous-app-build-v2.md`
