# Automaton MCP — Instructions

You are connected to Automaton via MCP. You can create and manage apps, write frontend and
backend code, and deploy Python scripts to runners — all without leaving your AI coding tool.

## First: Check for automaton.json

Before doing anything, check if `automaton.json` exists in the project root using your local
file tools.

**If it exists** — read `appId` from it. Use that appId in all tool calls. Do not create a
new app.

**If it does not exist** — call `create_app` with a short descriptive name. The tool returns
the appId. Immediately write it to `automaton.json` in the project root using your local file
tools:

```json
{
  "appId": "<returned-appId>",
  "name": "<app-name>",
  "server": "{server}"
}
```

`automaton.json` is the project's link to its Automaton app. Commit it to Git so collaborators
don't need to reconfigure anything.

## Tech Stack

- **Frontend**: HTML, CSS, JavaScript — any structure you prefer
  - Single page or multi-page (`index.html`, `about.html`, etc.)
  - Organize JS and CSS in any folders that make sense
  - Use any frontend libraries via CDN (no build step required)
- **Backend**: Python scripts in `backend/` — executed by Automaton's runner

## Backend Rules (strict — runner depends on these)

Controllers are Python files in `backend/`. The filename is the controller name.
Each action is a top-level function in that file.

```python
# backend/score.py
def get_top(inputs):
    limit = inputs.get('limit', 10)
    return {'scores': []}

def save(inputs):
    return {'saved': True}
```

**Rules:**
- `.py` files MUST be in `backend/` — never at the root or other locations
- Function name = action name (called from the frontend)
- Return a dict — it is serialized to JSON and returned to the frontend
- `backend/requirements.txt` — list pip dependencies here (optional)

## Calling a Controller from the Frontend

```javascript
// Calls backend/score.py → get_top()
const result = await window.automatonApi.callController('score', 'get_top', { limit: 5 });
```

`window.automatonApi` is injected by Automaton — do NOT define or override it yourself.

## Local Testing (without a running server)

Create mock response files so the app works offline:

```
backend/mocks/score.get_top.json   →  served when callController('score', 'get_top') is called locally
backend/mocks/score.save.json
```

Mock file format:
```json
{ "scores": [{ "player": "demo", "score": 42 }] }
```

The dev polyfill in `index.html` reads these automatically — no server needed for frontend testing.

## Workflow

1. Check for `automaton.json` → create app if missing
2. Build the frontend (`index.html` and supporting files)
3. Write backend controllers in `backend/*.py`
4. Write mock files in `backend/mocks/`
5. Call `deploy_backend` to push Python scripts to the runner

## Bulk Upload (alternative to file-by-file)

To push many files at once instead of writing them one by one, run from your terminal:

```bash
cd /path/to/app
zip -r /tmp/app.zip . --exclude "*.git*" --exclude "automaton.json"

# Merge — add/overwrite only the files in the zip (default)
curl -X POST "{server}/api/apps/<appId>/upload" \
  -H "X-API-Key: {apiKey}" \
  -F "file=@/tmp/app.zip"

# Full replacement — delete existing files first
curl -X POST "{server}/api/apps/<appId>/upload?clean=true" \
  -H "X-API-Key: {apiKey}" \
  -F "file=@/tmp/app.zip"
```

Response: `{ "written": [...], "skipped": [...], "errors": [...] }`

After a bulk upload containing `backend/*.py` files, call `deploy_backend`.
