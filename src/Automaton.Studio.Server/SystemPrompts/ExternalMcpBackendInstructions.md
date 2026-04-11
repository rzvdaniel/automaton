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

## File Location Rules

- Controller files MUST be in `backend/` — never at the root or any other location
- The controller name (used in `callController`) is the filename without `.py`
- `backend/requirements.txt` — list extra pip dependencies (optional)
- `data/` — place SQLite databases and data files here (created automatically)
- `backend/shared/` — shared Python modules imported by controllers (db, utils, etc.)

## Controller Pattern — Class + handle() Dispatch

Each domain gets one file. Group related actions in a class; dispatch via `handle()`.

```python
# backend/todos.py
import os, sys
sys.path.insert(0, os.path.dirname(__file__))  # enables: from shared.db import get_db

from shared.db import get_db
from datetime import datetime


class TodosController:

    def list(self, inputs: dict) -> dict:
        try:
            page  = int(inputs.get('page', 1))
            limit = int(inputs.get('limit', 25))
            with get_db() as db:
                rows  = db.execute(
                    "SELECT * FROM todos ORDER BY created_at DESC LIMIT ? OFFSET ?",
                    (limit, (page - 1) * limit)
                ).fetchall()
                total = db.execute("SELECT COUNT(*) FROM todos").fetchone()[0]
            return {"success": True, "data": {"items": [dict(r) for r in rows], "total": total}, "error": ""}
        except Exception as e:
            return {"success": False, "data": None, "error": str(e)}

    def create(self, inputs: dict) -> dict:
        try:
            title = inputs.get('title', '').strip()
            if not title:
                return {"success": False, "data": None, "error": "title is required"}
            with get_db() as db:
                cur = db.execute(
                    "INSERT INTO todos (title, done, created_at) VALUES (?, 0, ?)",
                    (title, datetime.utcnow().isoformat())
                )
            return {"success": True, "data": {"id": cur.lastrowid, "title": title, "done": False}, "error": ""}
        except Exception as e:
            return {"success": False, "data": None, "error": str(e)}

    def update(self, inputs: dict) -> dict:
        try:
            todo_id = inputs.get('id')
            if not todo_id:
                return {"success": False, "data": None, "error": "id is required"}
            fields = {k: v for k, v in inputs.items() if k != 'id' and v is not None}
            if not fields:
                return {"success": False, "data": None, "error": "no fields to update"}
            set_clause = ', '.join(f"{k} = ?" for k in fields)
            with get_db() as db:
                db.execute(f"UPDATE todos SET {set_clause} WHERE id = ?",
                           list(fields.values()) + [todo_id])
            return {"success": True, "data": {"id": todo_id, **fields}, "error": ""}
        except Exception as e:
            return {"success": False, "data": None, "error": str(e)}

    def delete(self, inputs: dict) -> dict:
        try:
            todo_id = inputs.get('id')
            if not todo_id:
                return {"success": False, "data": None, "error": "id is required"}
            with get_db() as db:
                db.execute("DELETE FROM todos WHERE id = ?", (todo_id,))
            return {"success": True, "data": {"deleted": todo_id}, "error": ""}
        except Exception as e:
            return {"success": False, "data": None, "error": str(e)}


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

DB_PATH = os.path.join(os.path.dirname(__file__), '../../data/app.db')

def get_db():
    """Return an open SQLite connection with row_factory set."""
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")
    return conn
```

**Table creation rules:**
- Always use `CREATE TABLE IF NOT EXISTS` — never drop existing tables
- Create tables in the controller's `__init__` or in a dedicated `_init_db()` called at module load

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
