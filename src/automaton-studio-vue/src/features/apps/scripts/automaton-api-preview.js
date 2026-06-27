/**
 * automatonApi — injected into every preview-mode app iframe.
 *
 * Placeholder tokens are substituted at runtime by buildPreviewApiScript() before
 * the script is written into the iframe document:
 *   __APP_ID__     → the app's UUID
 *   __BASE_URL__   → VITE_API_BASE_URL (e.g. https://localhost:7091)
 *   __AUTH_TOKEN__ → JWT bearer token (always present in preview mode)
 */
'use strict';

// Runtime-injected configuration — DO NOT edit these literal values here
const APP_ID = '__APP_ID__';
const BASE_URL = '__BASE_URL__';
const AUTH_TOKEN = '__AUTH_TOKEN__';
const TEST_RUN_ID = '__TEST_RUN_ID__';
const TEST_RUN_HEADER = 'X-Automaton-Test-Run-Id';
let USE_MOCK_BACKEND = __USE_MOCK_BACKEND__;

console.log('[AutomatonAPI] Preview mode initialized for app:', APP_ID);

function safeSerialize(value) {
  if (typeof value === 'string') return value;
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

function emitPreviewConsoleEvent(level, source, args) {
  try {
    const message = Array.isArray(args)
      ? args.map((item) => safeSerialize(item)).join(' ')
      : safeSerialize(args);

    window.parent.postMessage({
      type: 'previewConsoleEvent',
      data: {
        level,
        message,
        timestamp: new Date().toISOString(),
        source
      }
    }, '*');
  } catch {
    // Do not break preview execution if telemetry emit fails
  }
}

const originalConsole = {
  debug: console.debug.bind(console),
  log: console.log.bind(console),
  warn: console.warn.bind(console),
  error: console.error.bind(console)
};

console.debug = (...args) => {
  originalConsole.debug(...args);
  emitPreviewConsoleEvent('debug', 'preview-console', args);
};

console.log = (...args) => {
  originalConsole.log(...args);
  emitPreviewConsoleEvent('info', 'preview-console', args);
};

console.warn = (...args) => {
  originalConsole.warn(...args);
  emitPreviewConsoleEvent('warn', 'preview-console', args);
};

console.error = (...args) => {
  originalConsole.error(...args);
  emitPreviewConsoleEvent('error', 'preview-console', args);
};

// Notify the parent that a new preview session is starting.
// The parent clears its log buffer on this message so stale errors from the
// previous session are never visible after a page reload or navigation.
window.parent.postMessage({ type: 'previewConsoleClear' }, '*');

// ── Network request capture (for ui_network_log) ──────────────────────────
// Intercept fetch and XHR before any app code runs so all requests are captured.
const _networkLog = [];
const _MAX_NETWORK_LOG = 200;

function _pushNetworkEntry(entry) {
  _networkLog.push(entry);
  if (_networkLog.length > _MAX_NETWORK_LOG) {
    _networkLog.shift();
  }
}

const _originalFetch = window.fetch.bind(window);
window.fetch = async function(input, init) {
  const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : (input && input.url) || String(input);
  const method = ((init && init.method) || 'GET').toUpperCase();
  const start = Date.now();
  try {
    const response = await _originalFetch(input, init);
    _pushNetworkEntry({ type: 'fetch', url, method, status: response.status, ok: response.ok, duration: Date.now() - start, timestamp: new Date().toISOString() });
    return response;
  } catch (err) {
    _pushNetworkEntry({ type: 'fetch', url, method, status: null, ok: false, error: err && err.message, duration: Date.now() - start, timestamp: new Date().toISOString() });
    throw err;
  }
};

const _OriginalXHR = window.XMLHttpRequest;
window.XMLHttpRequest = function() {
  const xhr = new _OriginalXHR();
  let _xhrUrl = '';
  let _xhrMethod = 'GET';
  const _open = xhr.open.bind(xhr);
  const _start = { t: 0 };
  xhr.open = function(method, url, ...rest) {
    _xhrUrl = String(url);
    _xhrMethod = String(method).toUpperCase();
    _start.t = Date.now();
    return _open(method, url, ...rest);
  };
  xhr.addEventListener('loadend', function() {
    _pushNetworkEntry({ type: 'xhr', url: _xhrUrl, method: _xhrMethod, status: xhr.status || null, ok: xhr.status >= 200 && xhr.status < 300, duration: Date.now() - _start.t, timestamp: new Date().toISOString() });
  });
  return xhr;
};
try { window.XMLHttpRequest.prototype = _OriginalXHR.prototype; } catch { /* read-only in some envs */ }

async function apiCall(path, method, body) {
  const url = BASE_URL + path;
  console.log(`[AutomatonAPI] ${method} ${url}`);

  const options = {
    method,
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${AUTH_TOKEN}`,
      [TEST_RUN_HEADER]: TEST_RUN_ID
    }
  };

  if (body) {
    options.body = JSON.stringify(body);
  }

  const response = await fetch(url, options);
  const data = await response.json();

  if (!response.ok) {
    const error = new Error(data.error || data.message || 'Request failed');
    error.response = data;
    throw error;
  }

  return data;
}

async function executeFlow(flowIdentifier, { inputs = {}, runnerNames = null } = {}) {
  const startTime = Date.now();
  const path = `/api/web-apps/preview/${APP_ID}/flows/${encodeURIComponent(flowIdentifier)}/execute`;

  try {
    const result = await apiCall(path, 'POST', {
      inputs,
      runnerNames,
      testRunId: TEST_RUN_ID,
      useMockBackend: USE_MOCK_BACKEND
    });
    sendResultToConsole('flow', flowIdentifier, inputs, result, Date.now() - startTime, true);
    return result;
  } catch (err) {
    sendResultToConsole('flow', flowIdentifier, inputs, { success: false, status: 'Error', error: err.message }, Date.now() - startTime, false);
    throw err;
  }
}

async function executeController(controllerName, actionName, { inputs = {}, runnerNames = null } = {}) {
  const startTime = Date.now();
  const path = `/api/web-apps/preview/${APP_ID}/controllers/${encodeURIComponent(controllerName)}/actions/${encodeURIComponent(actionName)}/execute`;

  try {
    const result = await apiCall(path, 'POST', {
      inputs,
      runnerNames,
      testRunId: TEST_RUN_ID,
      useMockBackend: USE_MOCK_BACKEND
    });
    sendResultToConsole('controller', `${controllerName}.${actionName}`, inputs, result, Date.now() - startTime, true);
    return result;
  } catch (err) {
    sendResultToConsole('controller', `${controllerName}.${actionName}`, inputs, { success: false, status: 'Error', error: err.message }, Date.now() - startTime, false);
    throw err;
  }
}

function sendResultToConsole(type, name, inputs, result, duration, success) {
  try {
    window.parent.postMessage({
      type: 'automatonStepResult',
      data: {
        stepName: name,
        inputs,
        result,
        duration,
        timestamp: new Date().toISOString(),
        source: 'preview'
      }
    }, '*');
  } catch (e) {
    console.warn('[AutomatonAPI] Failed to send result to console:', e);
  }
}

async function waitForSelector(selector, timeoutMs = 3000, intervalMs = 100) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    const found = document.querySelector(selector);
    if (found) return found;
    await new Promise((resolve) => setTimeout(resolve, intervalMs));
  }
  return null;
}

function resolveSafePreviewNavigationTarget(targetUrl) {
  const trimmed = String(targetUrl || '').trim();
  if (!trimmed) return null;

  try {
    const resolved = new URL(trimmed, window.location.href);
    const sameOrigin = resolved.origin === window.location.origin;
    const runtimePathPrefix = `/a/${APP_ID}/`;
    const inRuntimePath = resolved.pathname.startsWith(runtimePathPrefix);
    return sameOrigin && inRuntimePath ? resolved.toString() : null;
  } catch {
    return null;
  }
}

// ── UI action handler registry ────────────────────────────────────────────────
// Each handler receives `args` and returns { success, ...rest } (no `action` field —
// the dispatcher injects it). To add a new tool, add one entry here; no other
// plumbing is required.

const uiActionHandlers = {
  async ui_navigate(args) {
    const url = String(args.url || '').trim();
    if (!url) return { success: false, error: "Missing required argument 'url'" };
    const safeTarget = resolveSafePreviewNavigationTarget(url);
    if (!safeTarget) {
      return { success: false, error: 'Blocked ui_navigate outside the current application runtime. Use relative app URLs only.' };
    }
    window.location.assign(safeTarget);
    return { success: true, details: `Navigated to ${safeTarget}` };
  },

  async ui_reload() {
    // Trigger a page reload. The parent side waits for the iframe's load event,
    // so this just schedules the reload and returns before the navigation starts
    // (JS is single-threaded; postMessage is dispatched before the page unloads).
    window.location.reload();
    return { success: true, details: 'Reload triggered' };
  },

  async ui_click(args) {
    const selector = String(args.selector || '').trim();
    if (!selector) return { success: false, error: "Missing required argument 'selector'" };
    const element = document.querySelector(selector);
    if (!element) return { success: false, error: `Element not found for selector: ${selector}` };
    element.click();
    return { success: true, details: `Clicked ${selector}` };
  },

  async ui_type(args) {
    const selector = String(args.selector || '').trim();
    const text = String(args.text || '');
    if (!selector) return { success: false, error: "Missing required argument 'selector'" };
    const element = document.querySelector(selector);
    if (!element || (element.tagName !== 'INPUT' && element.tagName !== 'TEXTAREA')) {
      return { success: false, error: `Input element not found for selector: ${selector}` };
    }
    element.focus();
    element.value = text;
    element.dispatchEvent(new Event('input', { bubbles: true }));
    element.dispatchEvent(new Event('change', { bubbles: true }));
    return { success: true, details: `Typed into ${selector}` };
  },

  async ui_read_text(args) {
    const selector = String(args.selector || '').trim();
    if (!selector) return { success: false, error: "Missing required argument 'selector'" };
    const element = document.querySelector(selector);
    if (!element) return { success: false, error: `Element not found for selector: ${selector}` };
    const value = (element.textContent || '').trim();
    return { success: true, value, details: `Read text from ${selector}` };
  },

  async ui_wait_for(args) {
    const selector = String(args.selector || '').trim();
    const timeoutMs = Number(args.timeoutMs || 3000);
    const intervalMs = Number(args.intervalMs || 100);
    if (!selector) return { success: false, error: "Missing required argument 'selector'" };
    const found = await waitForSelector(selector, timeoutMs, intervalMs);
    if (!found) {
      return { success: false, error: `Timed out waiting for selector: ${selector} (${timeoutMs}ms)` };
    }
    return { success: true, details: `Selector appeared: ${selector}` };
  },

  async ui_assert(args) {
    const selector = String(args.selector || '').trim();
    const exists = args.exists === undefined ? true : Boolean(args.exists);
    const containsText = args.containsText === undefined ? undefined : String(args.containsText);
    const equalsText = args.equalsText === undefined ? undefined : String(args.equalsText);
    if (!selector) return { success: false, error: "Missing required argument 'selector'" };

    const element = document.querySelector(selector);
    const found = Boolean(element);
    if (exists !== found) {
      return { success: false, error: `Assertion failed for ${selector}: expected exists=${exists}, actual=${found}` };
    }
    if (element && containsText !== undefined) {
      const text = (element.textContent || '').trim();
      if (!text.includes(containsText)) {
        return { success: false, error: `Assertion failed for ${selector}: text does not include "${containsText}"` };
      }
    }
    if (element && equalsText !== undefined) {
      const text = (element.textContent || '').trim();
      if (text !== equalsText) {
        return { success: false, error: `Assertion failed for ${selector}: expected "${equalsText}", got "${text}"` };
      }
    }
    return { success: true, details: `Assertion passed for ${selector}` };
  },

  async ui_get_page_snapshot(args) {
    const maxChars = Math.max(100, Number(args.maxChars || 1000));
    const title = document.title || '';
    const url = window.location.href || '';
    const bodyText = (document.body?.innerText || '').slice(0, maxChars);
    return { success: true, details: `Snapshot captured (${bodyText.length} chars)`, snapshot: { title, url, bodyText } };
  },

  async ui_set_backend_mode(args) {
    const mode = String(args.mode || '').toLowerCase();
    if (mode !== 'mock' && mode !== 'real') {
      return { success: false, error: "Invalid mode. Use 'mock' or 'real'" };
    }
    if (!window.automatonApi) {
      return { success: false, error: 'automatonApi is not available in this preview' };
    }
    const confirmed = window.automatonApi.setBackendMode(mode);
    return { success: true, details: `Backend mode set to '${confirmed}'`, mode: confirmed };
  },

  async ui_evaluate(args) {
    const expression = String(args.expression || '').trim();
    if (!expression) return { success: false, error: "Missing required argument 'expression'" };
    try {
      // Function() evaluates in the global scope — has access to window variables
      // eslint-disable-next-line no-new-func
      const result = Function('"use strict"; return (' + expression + ')')();
      let serialized;
      try { serialized = JSON.stringify(result); } catch { serialized = String(result); }
      return { success: true, value: serialized, details: `Evaluated: ${expression}` };
    } catch (err) {
      return { success: false, error: err && err.message ? err.message : 'Evaluation failed' };
    }
  },

  async ui_keyboard(args) {
    const key = String(args.key || '').trim();
    if (!key) return { success: false, error: "Missing required argument 'key'" };
    const selector = args.selector !== undefined ? String(args.selector).trim() : '';
    const repeat = Math.max(1, Math.min(20, Number(args.repeat || 1)));
    const ctrlKey = Boolean(args.ctrlKey);
    const shiftKey = Boolean(args.shiftKey);
    const altKey = Boolean(args.altKey);
    let target = document.body;
    if (selector) {
      const el = document.querySelector(selector);
      if (!el) return { success: false, error: `Element not found for selector: ${selector}` };
      target = el;
      target.focus();
    }
    const init = { key, bubbles: true, cancelable: true, ctrlKey, shiftKey, altKey };
    for (let i = 0; i < repeat; i++) {
      target.dispatchEvent(new KeyboardEvent('keydown', init));
      target.dispatchEvent(new KeyboardEvent('keypress', init));
      target.dispatchEvent(new KeyboardEvent('keyup', init));
    }
    return { success: true, details: `Dispatched key '${key}' \u00d7 ${repeat}${selector ? ` to ${selector}` : ' to document.body'}` };
  },

  async ui_get_attribute(args) {
    const selector = String(args.selector || '').trim();
    const attributes = Array.isArray(args.attributes) ? args.attributes.map(String) : [];
    if (!selector) return { success: false, error: "Missing required argument 'selector'" };
    if (!attributes.length) return { success: false, error: "Missing required argument 'attributes'" };
    const element = document.querySelector(selector);
    if (!element) return { success: false, error: `Element not found for selector: ${selector}` };
    const result = {};
    for (const attr of attributes) {
      result[attr] = element.getAttribute(attr);
    }
    return { success: true, attributes: result, details: `Read ${attributes.length} attribute(s) from ${selector}` };
  },

  async ui_query_all(args) {
    const selector = String(args.selector || '').trim();
    if (!selector) return { success: false, error: "Missing required argument 'selector'" };
    const includeText = Boolean(args.includeText);
    const limit = Math.min(50, Math.max(1, Number(args.limit || 20)));
    const elements = Array.from(document.querySelectorAll(selector));
    const count = elements.length;
    const items = includeText ? elements.slice(0, limit).map(el => (el.textContent || '').trim()) : undefined;
    return { success: true, count, items, details: `Found ${count} element(s) for selector: ${selector}` };
  },

  async ui_select(args) {
    const selector = String(args.selector || '').trim();
    const value = String(args.value !== undefined ? args.value : '');
    if (!selector) return { success: false, error: "Missing required argument 'selector'" };
    const element = document.querySelector(selector);
    if (!element || element.tagName !== 'SELECT') {
      return { success: false, error: `Select element not found for selector: ${selector}` };
    }
    element.value = value;
    element.dispatchEvent(new Event('input', { bubbles: true }));
    element.dispatchEvent(new Event('change', { bubbles: true }));
    return { success: true, details: `Selected '${value}' in ${selector}` };
  },

  async ui_hover(args) {
    const selector = String(args.selector || '').trim();
    if (!selector) return { success: false, error: "Missing required argument 'selector'" };
    const element = document.querySelector(selector);
    if (!element) return { success: false, error: `Element not found for selector: ${selector}` };
    element.dispatchEvent(new MouseEvent('mouseover', { bubbles: true, cancelable: true }));
    element.dispatchEvent(new MouseEvent('mouseenter', { bubbles: false, cancelable: false }));
    element.focus();
    return { success: true, details: `Hovered over ${selector}` };
  },

  async ui_scroll_to(args) {
    const selector = String(args.selector || '').trim();
    if (!selector) return { success: false, error: "Missing required argument 'selector'" };
    const behavior = args.behavior === 'smooth' ? 'smooth' : 'instant';
    const element = document.querySelector(selector);
    if (!element) return { success: false, error: `Element not found for selector: ${selector}` };
    element.scrollIntoView({ behavior, block: 'start' });
    return { success: true, details: `Scrolled to ${selector}` };
  },

  async ui_check(args) {
    const selector = String(args.selector || '').trim();
    const checked = args.checked === undefined ? true : Boolean(args.checked);
    if (!selector) return { success: false, error: "Missing required argument 'selector'" };
    const element = document.querySelector(selector);
    if (!element || (element.type !== 'checkbox' && element.type !== 'radio')) {
      return { success: false, error: `Checkbox/radio not found for selector: ${selector}` };
    }
    if (element.checked !== checked) {
      element.checked = checked;
      element.dispatchEvent(new Event('change', { bubbles: true }));
      element.dispatchEvent(new Event('input', { bubbles: true }));
    }
    return { success: true, details: `${checked ? 'Checked' : 'Unchecked'} ${selector}` };
  },

  async ui_get_canvas_data(args) {
    const selector = String(args.selector || 'canvas').trim();
    const sampleSize = Math.max(1, Math.min(16, Number(args.sampleSize || 4)));
    const canvas = document.querySelector(selector);
    if (!canvas || canvas.tagName !== 'CANVAS') {
      return { success: false, error: `Canvas not found for selector: ${selector}` };
    }
    const { width, height } = canvas;
    if (!width || !height) {
      return { success: false, error: `Canvas has zero dimensions (${width}x${height})` };
    }
    let pixels = null;
    // Try 2D context
    const ctx2d = canvas.getContext('2d');
    if (ctx2d) {
      try { pixels = ctx2d.getImageData(0, 0, width, height).data; } catch { /* tainted */ }
    }
    // Try WebGL if 2D didn't yield data
    if (!pixels) {
      const gl = canvas.getContext('webgl') || canvas.getContext('webgl2') || canvas.getContext('experimental-webgl');
      if (gl) {
        try {
          const buf = new Uint8Array(width * height * 4);
          gl.readPixels(0, 0, width, height, gl.RGBA, gl.UNSIGNED_BYTE, buf);
          pixels = buf;
        } catch { /* ignore */ }
      }
    }
    if (!pixels) {
      return { success: false, error: 'Could not read pixel data (tainted canvas or unsupported context type)' };
    }
    const stepX = Math.max(1, Math.floor(width / sampleSize));
    const stepY = Math.max(1, Math.floor(height / sampleSize));
    let hasNonBlackPixels = false;
    let totalAlpha = 0;
    let sampleCount = 0;
    for (let row = 0; row < sampleSize; row++) {
      for (let col = 0; col < sampleSize; col++) {
        const x = Math.min(col * stepX, width - 1);
        const y = Math.min(row * stepY, height - 1);
        const i = (y * width + x) * 4;
        if (pixels[i] > 0 || pixels[i + 1] > 0 || pixels[i + 2] > 0) hasNonBlackPixels = true;
        totalAlpha += pixels[i + 3];
        sampleCount++;
      }
    }
    const sampleAlpha = sampleCount > 0 ? Math.round(totalAlpha / sampleCount) : 0;
    return {
      success: true,
      width,
      height,
      hasNonBlackPixels,
      sampleAlpha,
      details: hasNonBlackPixels
        ? `Canvas has non-black pixels (${width}\u00d7${height})`
        : `Canvas appears empty/black (${width}\u00d7${height})`,
    };
  },

  async ui_network_log(args) {
    const limit = Math.min(100, Math.max(1, Number(args.limit || 20)));
    const urlPattern = args.urlPattern !== undefined ? String(args.urlPattern) : '';
    let entries = _networkLog.slice();
    if (urlPattern) {
      entries = entries.filter(e => e.url && e.url.includes(urlPattern));
    }
    const returned = entries.slice(-limit);
    return {
      success: true,
      count: entries.length,
      entries: returned,
      details: `Returned ${returned.length} of ${entries.length} network request(s)${urlPattern ? ` matching '${urlPattern}'` : ''}`,
    };
  },
};

async function executePreviewAutomationAction(action, args = {}) {
  const handler = uiActionHandlers[action];
  if (!handler) {
    return { success: false, action, error: `Unsupported UI action in preview bridge: ${action}` };
  }
  try {
    const result = await handler(args);
    return { action, ...result };
  } catch (err) {
    return {
      success: false,
      action,
      error: err && err.message ? err.message : 'Unknown UI automation error'
    };
  }
}

async function callController(controllerName, actionName, inputs = {}) {
  const res = await executeController(controllerName, actionName, { inputs });
  if (!res.success) throw new Error(res.error || 'Request failed');
  return res.data;
}

/**
 * Request a fresh JWT from the parent editor window via postMessage.
 * The parent always replies, so no timeout fallback is needed.
 */
async function getFreshToken() {
  return new Promise((resolve) => {
    const requestId = Math.random().toString(36).slice(2);

    function onMessage(event) {
      if (event.data?.type === 'tokenResponse' && event.data?.requestId === requestId) {
        window.removeEventListener('message', onMessage);
        resolve(event.data.token || AUTH_TOKEN);
      }
    }

    window.addEventListener('message', onMessage);
    window.parent.postMessage({ type: 'requestToken', requestId }, '*');
  });
}

async function uploadFile(file, { runnerNames = null } = {}) {
  const url = `${BASE_URL}/api/web-apps/preview/${APP_ID}/files/upload`;
  console.log(`[AutomatonAPI] Uploading file: ${file.name} (${file.size} bytes)`);

  const token = await getFreshToken();
  const formData = new FormData();
  formData.append('file', file);

  const response = await fetch(url, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`
    },
    body: formData
  });

  const data = await response.json();
  if (!response.ok) {
    throw new Error(data.error || 'File upload failed');
  }

  console.log(`[AutomatonAPI] Upload complete: session_id=${data.session_id}`);
  return data; // { session_id }
}

function getDownloadUrl(sessionId) {
  return `${BASE_URL}/api/web-apps/preview/${APP_ID}/files/${sessionId}`;
}

/**
 * Download a result file from the runner.
 * Uses fetch + Blob URL so the Authorization header is included automatically.
 * This is the recommended way for AI-generated apps to trigger downloads.
 */
async function downloadFile(sessionId, filename) {
  const token = await getFreshToken();
  const url = getDownloadUrl(sessionId);

  const response = await fetch(url, {
    headers: { 'Authorization': `Bearer ${token}` }
  });

  if (!response.ok) {
    throw new Error(`Download failed: ${response.status}`);
  }

  // Derive filename from Content-Disposition if not provided
  const disposition = response.headers.get('content-disposition') || '';
  const name = filename || disposition.match(/filename="?([^";\n]+)/i)?.[1] || 'download';

  const blob = await response.blob();
  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = objectUrl;
  anchor.download = name;
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(objectUrl), 10000);
}

window.automatonApi = {
  executeFlow,
  callController,
  uploadFile,
  downloadFile,
  getAppId: () => APP_ID,
  getAppSlug: () => null,
  getBaseUrl: () => BASE_URL,
  getTestRunId: () => TEST_RUN_ID,
  getBackendMode: () => (USE_MOCK_BACKEND ? 'mock' : 'real'),
  setBackendMode: (mode) => {
    USE_MOCK_BACKEND = String(mode).toLowerCase() !== 'real';
    return USE_MOCK_BACKEND ? 'mock' : 'real';
  }
};

window.addEventListener('error', (event) => {
  try {
    const target = event.target || event.srcElement;
    if (!target) return;

    const url = target.src || target.href;
    if (!url) return;

    window.parent.postMessage({
      type: 'previewResourceError',
      data: {
        url,
        tagName: (target.tagName || '').toLowerCase()
      }
    }, '*');
  } catch (e) {
    // Ignore preview error reporting failures
  }
}, true);

window.addEventListener('error', (event) => {
  emitPreviewConsoleEvent('error', 'preview-runtime', [
    event.message || 'Runtime error',
    event.filename ? `at ${event.filename}:${event.lineno || 0}:${event.colno || 0}` : ''
  ]);
});

window.addEventListener('unhandledrejection', (event) => {
  const reason = event.reason?.message || event.reason || 'Unhandled promise rejection';
  emitPreviewConsoleEvent('error', 'preview-runtime', [reason]);
});

window.addEventListener('message', async (event) => {
  const payload = event.data || {};
  if (payload.type !== 'previewAutomationRequest' || !payload.requestId) {
    return;
  }

  const result = await executePreviewAutomationAction(payload.action, payload.args || {});

  window.parent.postMessage({
    type: 'previewAutomationResponse',
    requestId: payload.requestId,
    result
  }, '*');
});

console.log('[AutomatonAPI] Ready!');
