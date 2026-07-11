# Automaton HTTP Backends

Automaton backends are ordinary HTTP applications stored in `backend/`.

Rules:

- The initial supported convention is FastAPI/ASGI with an entrypoint of `main:app`.
- Expose normal resource endpoints under `/api`.
- Frontends call same-origin root-relative URLs such as `fetch('/api/todos')`.
- Keep secrets, persistence, and server-side logic in the backend.
- Keep shared helpers in `backend/` and dependencies in `backend/requirements.txt`.
- Use appropriate HTTP methods, status codes, and JSON responses.
- For Python diagnostics, prefer the standard `logging` module over `print(...)`.

Recommended pattern:

```python
import logging
from fastapi import FastAPI

logger = logging.getLogger("todos")
app = FastAPI()

@app.get("/api/todos")
def list_todos():
    logger.info("Listing todos")
    return {"items": []}

@app.post("/api/todos")
def create_todo(todo: dict):
    return {"todo": todo}
```

The runner owns the backend process and proxies the app's `/api` paths. Published apps use an
app-specific origin derived from the slug, so the browser should not need an app ID or special
deployment path.
