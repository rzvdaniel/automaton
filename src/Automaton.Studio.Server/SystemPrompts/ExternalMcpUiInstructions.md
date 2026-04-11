# UI Rules

## Styling — Always Use Tailwind

Include Tailwind CSS via CDN in every HTML file. Never write plain unstyled HTML.

```html
<script src="https://cdn.tailwindcss.com"></script>
```

## File Structure

**Use separate `.html` files** for pages the user navigates between. Each distinct destination = its own file.

**Use JS-driven show/hide** only for in-page state: tabs, step wizards, modals.

**Decision rule**: if the user would type a different URL to reach it, it's a separate `.html` file.

Typical structure:
```
├─ index.html              # Entry point / dashboard
├─ pages/                  # Additional top-level pages
├─ backend/                # Python controllers
├─ data/                   # SQLite database
├─ features/               # Feature JS modules (one folder per domain)
├─ shared/
│  ├─ components/          # modal.js, toast.js, table.js
│  └─ utils/               # format.js, validators.js
└─ js/
   ├─ api.js               # All callController wrappers
   └─ app.js               # Router / module loader
```

## JavaScript — Browser ESM Only

- **ESM only** — never use `require`, `module.exports`, or `exports.*`
- Use `<script type="module">` for all JS files
- Use `data-*` attributes as JS hooks — never select by CSS class

```html
<button class="..." data-action="save" data-item-id="42">Save</button>
```
```javascript
document.querySelector('[data-action="save"]').addEventListener('click', handleSave);
```

## Calling a Controller from the Frontend

`callController` returns `res.data` — the unwrapped data object from the backend.
It throws on error, so always wrap in `try/catch`. Never check `result.success` or access `result.data`.

```javascript
// If backend returns {"success": true, "data": {"items": [...], "total": 5}, "error": ""}
// callController returns {"items": [...], "total": 5}  ← result IS the data

try {
    const { items, total } = await window.automatonApi.callController('todos', 'list', {});
    renderList(items);
} catch (err) {
    showToast(err.message, 'error');  // errors surface as thrown exceptions
}

// ❌ WRONG — result has no .success or .data wrapper
const res = await window.automatonApi.callController('todos', 'list', {});
if (res.success) { ... }       // .success doesn't exist
renderList(res.data.items);    // .data doesn't exist — result IS the data
```
