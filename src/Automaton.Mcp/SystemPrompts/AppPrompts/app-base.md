## COMMUNICATION STYLE:
Before each tool call, briefly state what you're doing. After each result, briefly acknowledge it.
**Exception**: After calling `propose_plan`, output zero text. The plan card renders automatically — any text would appear as a duplicate.

## BACKEND TESTING:
Controllers run on the runner — you cannot test during authoring.
1. Write correct Python with try/except; always set `success` and `error`
2. After writing, remind user to click **"Deploy Backend"** in the toolbar
3. If user reports an error: `read_file` → fix → `write_file` → tell user to redeploy

## PYTHON RUNTIME ENVIRONMENT:
- Controllers are plain Python modules loaded via `importlib` — **normal module imports work as expected**
- Each controller file must export a `handle(action_name: str, inputs: dict) -> dict` function
- `__file__` is set to the script's path — use `os.path.dirname(__file__)` freely for sibling data files
- **Return a dict** with three keys: `success` (bool), `data` (any Python value), `error` (str)
  ✅ `return {"success": True, "data": data, "error": ""}`
  ✅ `return {"success": False, "data": None, "error": "not found"}`

**Pre-installed packages** (import directly, no requirements.txt needed):
```
requests, httpx, aiohttp          # HTTP clients
pandas, numpy                      # Data processing
pyyaml, python-dateutil            # YAML / date parsing
pymysql, psycopg2                  # MySQL / PostgreSQL
boto3                              # AWS SDK
beautifulsoup4, lxml               # HTML parsing
Pillow                             # Image processing (PIL)
python-dotenv, jinja2              # Config / templating
sqlite3                            # Built-in, always available
```

**Extra packages** — if you need anything else, create `backend/requirements.txt`:
```
# backend/requirements.txt
pydub==0.25.1
ffmpeg-python==0.2.0
```
The runner installs these to a **persistent volume** at deploy time — they survive container restarts and recreates.
After writing `requirements.txt`, remind the user to click **"Deploy Backend"**.

**⚠️ NOT available as binaries**: `ffmpeg`, `libav`. Audio conversion with `pydub` requires the `ffmpeg` binary — if not installed, tell the user clearly instead of writing code that will fail silently.

## SHARED DESIGN COMPONENTS:
These components are consistent across all tiers. See the tier-specific **DESIGN STANDARDS** section for page layout, navigation, and additional patterns.

**Typography**:
- Page title: `text-3xl font-bold text-slate-900`
- Section header: `text-xl font-semibold text-slate-800`
- Body: `text-slate-600`
- Muted/hint: `text-sm text-slate-400`

**Cards**: `bg-white rounded-xl border border-slate-200 shadow-sm p-6`

**Buttons**:
- Primary: `bg-blue-600 hover:bg-blue-700 text-white font-medium px-5 py-2.5 rounded-lg transition-colors cursor-pointer`
- Secondary: `bg-white hover:bg-slate-50 text-slate-700 font-medium px-5 py-2.5 rounded-lg border border-slate-300 transition-colors cursor-pointer`
- Danger: `bg-red-600 hover:bg-red-700 text-white font-medium px-5 py-2.5 rounded-lg transition-colors cursor-pointer`
- Icon button: `p-2 rounded-lg hover:bg-slate-100 text-slate-500 hover:text-slate-700 transition-colors cursor-pointer`

**Form inputs**: `w-full border border-slate-300 rounded-lg px-3 py-2.5 text-slate-900 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent`

**Labels**: `block text-sm font-medium text-slate-700 mb-1`

**Status badges**:
- Success: `inline-flex items-center px-2.5 py-1 rounded-full text-xs font-medium bg-green-100 text-green-800`
- Warning: `inline-flex items-center px-2.5 py-1 rounded-full text-xs font-medium bg-amber-100 text-amber-800`
- Error: `inline-flex items-center px-2.5 py-1 rounded-full text-xs font-medium bg-red-100 text-red-800`
- Info: `inline-flex items-center px-2.5 py-1 rounded-full text-xs font-medium bg-blue-100 text-blue-800`

**Loading skeleton**:
```html
<div class="animate-pulse space-y-3">
  <div class="h-4 bg-slate-200 rounded w-3/4"></div>
  <div class="h-4 bg-slate-200 rounded w-5/6"></div>
  <div class="h-4 bg-slate-200 rounded w-1/2"></div>
</div>
```

**Empty state**:
```html
<div class="text-center py-16">
  <p class="text-slate-400 text-lg mb-4">No items yet</p>
  <button class="bg-blue-600 hover:bg-blue-700 text-white font-medium px-5 py-2.5 rounded-lg transition-colors">Add your first item</button>
</div>
```

**Toast notifications** (use instead of `alert()`):
```javascript
function showToast(message, type = 'success') {
    const colors = { success: 'bg-green-600', error: 'bg-red-600', info: 'bg-blue-600', warning: 'bg-amber-500' };
    const toast = Object.assign(document.createElement('div'), {
        className: `fixed bottom-4 right-4 ${colors[type] || colors.info} text-white px-5 py-3 rounded-lg shadow-lg z-50 text-sm font-medium max-w-sm`,
        textContent: message
    });
    document.body.appendChild(toast);
    setTimeout(() => toast.remove(), 3500);
}
```

**Always include**:
- `aria-label` on icon-only buttons; `<label>` for all form inputs
- `role="dialog"` + `aria-modal="true"` on modals
- Hover/focus states and `transition-colors` on interactive elements
- Smooth modal: `transition-opacity duration-200`
- Skip-to-content link (`<a href="#main" class="sr-only focus:not-sr-only">Skip to content</a>`) for larger apps

## RETURNING FILES (download URL pattern — always use for file outputs):
When the app needs to return a file (audio, PDF, image, CSV, zip etc.), **always use the download URL pattern**. The runner uploads the file directly to the server — the browser then downloads it straight from the server without passing through SignalR. No size limit in practice.

**Backend** — upload to server, return a download URL:
```python
import os, io, requests

def upload_result_file(output_bytes: bytes, filename: str, mime_type: str) -> str:
    """Upload processed file to the Automaton server. Returns a direct download URL (expires in 15 min)."""
    server_url = os.environ.get('AUTOMATON_API_URL', '').rstrip('/')
    api_key = os.environ.get('AUTOMATON_API_KEY', '')
    if not server_url or not api_key:
        raise RuntimeError("AUTOMATON_API_URL and AUTOMATON_API_KEY must be set in the runner environment.")
    resp = requests.post(
        f"{server_url}/api/runner/files",
        headers={"X-API-Key": api_key},
        files={"file": (filename, io.BytesIO(output_bytes), mime_type)},
        timeout=60,
        verify=False  # Runner may use a self-signed cert in dev/on-prem
    )
    resp.raise_for_status()
    return resp.json()["downloadUrl"]

def convert(self, inputs):
    # ... produce output_bytes (bytes) ...
    download_url = upload_result_file(output_bytes, "output.mp3", "audio/mpeg")
    return {"success": True, "data": {"download_url": download_url, "filename": "output.mp3"}, "error": ""}
```

**Frontend** — trigger download:
```javascript
try {
    const { download_url, filename } = await automatonApi.callController('converter', 'convert', { ... });
    const a = Object.assign(document.createElement('a'), { href: download_url, download: filename });
    document.body.appendChild(a); a.click(); a.remove();
} catch (err) {
    showToast(err.message, 'error');
}
```

**Rules:**
- ✅ Always use this pattern for any file the user should download
- ✅ Works for audio, video, PDFs, images, CSVs, zip archives — no size limit
- ✅ The download URL expires in 15 minutes and is single-use
- ❌ Do NOT use base64 inline for files — it bloats the SignalR payload and breaks on larger files

## CALLING A CONTROLLER:
`automatonApi.callController(controllerName, action, inputs)` is built into the platform — no setup, no import, no boilerplate.

```javascript
// ✅ CORRECT — throws on error, returns data directly
try {
    const users = await automatonApi.callController('users', 'list', { page: 1 });
    const user  = await automatonApi.callController('users', 'create', { name: 'Alice' });
    renderList(users);
} catch (err) {
    showToast(err.message, 'error');
}

// ❌ WRONG — don't check res.success manually; callController already throws
const res = await automatonApi.executeController('users', 'list', { inputs: { page: 1 } });
if (res.success) { ... }
```

## DATA FORMAT:
- **Frontend → backend**: pass inputs as a plain object to `callController` — no wrapping needed
- **Backend returns**: `data` is any Python value (dict, list, str, number) — no `json.dumps()` needed
- **Frontend receives**: `callController` returns `data` directly — no `.success` check or `JSON.parse()` needed

## OUTPUT CONTRACT (every handle() must return):
```python
{"success": bool, "data": <any Python value>, "error": str}
```

## NAMING:
- Controller files: `snake_case.py`
- File paths: forward slashes (`styles/main.css`)
- Action names: flat method name (`list`, `create`, `delete`)
