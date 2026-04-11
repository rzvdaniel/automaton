# Backend Rules

## Python Runtime Environment

Controllers are Python modules in `backend/`. The runner loads each file via `importlib` —
normal imports work exactly as expected. `__file__` is set to the script path, so you can use
`os.path.dirname(__file__)` to locate sibling files (e.g. `shared/db.py`, `data/app.db`).

**Every controller must export a `handle` function:**
```python
def handle(action_name: str, inputs: dict) -> dict:
    ...
```

**Every action must return exactly three keys:**
```python
# ✅ Success
return {"success": True, "data": data, "error": ""}

# ✅ Failure
return {"success": False, "data": None, "error": "description of what went wrong"}
```

Never return a bare dict without these keys — the frontend and the runner both expect them.

**❌ NEVER flatten `data` to the top level in `handle()`:**
```python
# ❌ WRONG — this removes the 'data' key; callController returns res.data, which becomes undefined
if result.get("success") and result.get("data"):
    return {**result["data"], "success": True, "error": ""}

# ✅ CORRECT — return method result as-is; it already has the right shape
return method(inputs)
```

## File Location Rules

- Controller files MUST be in `backend/` — never at the root or any other location
- The controller name (used in `callController`) is the filename without `.py`
- `backend/requirements.txt` — list extra pip dependencies (optional)
- `data/` — place SQLite databases and data files here (created automatically)
- `backend/shared/` — shared Python modules imported by controllers (db, utils, etc.)

## Controller Pattern — Class + handle() Dispatch

Each domain gets one file. Group related actions in a class; dispatch via `handle()`.

Use the `_action` decorator to avoid repeating `try/except` in every method:

```python
# backend/todos.py
import os, sys
sys.path.insert(0, os.path.dirname(__file__))

from shared.db import get_db
from datetime import datetime, timezone
import functools


def _action(fn):
    """Wrap a controller method: catch exceptions and return the standard error shape."""
    @functools.wraps(fn)
    def wrapper(self, inputs):
        try:
            return fn(self, inputs)
        except Exception as e:
            return {"success": False, "data": None, "error": str(e)}
    return wrapper


class TodosController:

    @_action
    def list(self, inputs: dict) -> dict:
        page  = int(inputs.get('page', 1))
        limit = int(inputs.get('limit', 25))
        with get_db() as db:
            rows  = db.execute(
                "SELECT * FROM todos ORDER BY created_at DESC LIMIT ? OFFSET ?",
                (limit, (page - 1) * limit)
            ).fetchall()
            total = db.execute("SELECT COUNT(*) FROM todos").fetchone()[0]
        return {"success": True, "data": {"items": [dict(r) for r in rows], "total": total}, "error": ""}

    @_action
    def create(self, inputs: dict) -> dict:
        title = inputs.get('title', '').strip()
        if not title:
            return {"success": False, "data": None, "error": "title is required"}
        with get_db() as db:
            cur = db.execute(
                "INSERT INTO todos (title, done, created_at) VALUES (?, 0, ?)",
                (title, datetime.now(timezone.utc).isoformat())
            )
        return {"success": True, "data": {"id": cur.lastrowid, "title": title, "done": False}, "error": ""}

    @_action
    def update(self, inputs: dict) -> dict:
        todo_id = inputs.get('id')
        if not todo_id:
            return {"success": False, "data": None, "error": "id is required"}
        # ❌ NEVER build SET clauses from user-supplied key names — SQL injection risk.
        # ✅ Always enumerate the exact columns you allow:
        ALLOWED = {'title', 'done'}
        fields = {k: v for k, v in inputs.items() if k in ALLOWED and v is not None}
        if not fields:
            return {"success": False, "data": None, "error": "no valid fields to update"}
        set_clause = ', '.join(f"{k} = ?" for k in fields)
        with get_db() as db:
            db.execute(f"UPDATE todos SET {set_clause} WHERE id = ?",
                       [*fields.values(), todo_id])
        return {"success": True, "data": {"id": todo_id, **fields}, "error": ""}

    @_action
    def delete(self, inputs: dict) -> dict:
        todo_id = inputs.get('id')
        if not todo_id:
            return {"success": False, "data": None, "error": "id is required"}
        with get_db() as db:
            db.execute("DELETE FROM todos WHERE id = ?", (todo_id,))
        return {"success": True, "data": {"deleted": todo_id}, "error": ""}


# ── Dispatch ──────────────────────────────────────────────────────────────
_controller = TodosController()

def handle(action_name: str, inputs: dict) -> dict:
    method = getattr(_controller, action_name, None)
    if method is None:
        available = [m for m in dir(_controller) if not m.startswith('_')]
        return {"success": False, "data": None,
                "error": f"Unknown action '{action_name}'. Available: {available}"}
    return method(inputs)
```

## Data Persistence — SQLite (default)

All controllers share one database at `../data/app.db` (relative to the `backend/` folder).
Initialise the schema in `backend/shared/db.py`:

```python
# backend/shared/db.py
import sqlite3, os
from contextlib import contextmanager

DB_PATH = os.path.join(os.path.dirname(__file__), '../../data/app.db')

@contextmanager
def get_db():
    """Open a SQLite connection, commit on success, rollback on error, always close."""
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")
    try:
        yield conn
        conn.commit()
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.close()
```

Using `@contextmanager` ensures the connection is **always closed** after the `with` block.
The plain `sqlite3.connect().__enter__` / `__exit__` only commits/rolls back — it does not close.

**Table creation rules:**
- Always use `CREATE TABLE IF NOT EXISTS` — never drop existing tables
- Create tables in a module-level `_init_db()` called at import time

```python
def _init_db():
    with get_db() as db:
        db.execute("""
            CREATE TABLE IF NOT EXISTS todos (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                title      TEXT    NOT NULL,
                done       INTEGER NOT NULL DEFAULT 0,
                created_at TEXT    NOT NULL
            )
        """)

_init_db()
```

## Other Databases

- **PostgreSQL**: Use `psycopg2`. Get connection string from the user.
- **MySQL**: Use `mysql-connector-python`. Only if the user provides credentials.

## Pre-installed Packages

These are available directly — no `requirements.txt` entry needed:

```
requests, httpx, aiohttp          # HTTP clients
pandas, numpy                      # Data processing
pyyaml, python-dateutil            # YAML / date parsing
pymysql, psycopg2                  # MySQL / PostgreSQL
boto3                              # AWS SDK
beautifulsoup4, lxml               # HTML parsing
Pillow                             # Image processing (PIL)
python-dotenv, jinja2              # Config / templating
sqlite3                            # Built-in standard library
```

For anything else, create `backend/requirements.txt`:
```
# backend/requirements.txt
httpx==0.27.0
some-other-package==1.2.3
```

The runner installs these at deploy time. Always remind the user to **Deploy Backend**
after adding or changing `requirements.txt`.

## When Backend is Required

**Yes:** external APIs, database reads/writes, auth, file system access, server-side
calculations, data aggregation, form submissions (always persist to SQLite or process
server-side).

**No:** pure UI interactions, client-side calculations, client-side filtering/sorting of
already-fetched data, fully static content with no forms.
