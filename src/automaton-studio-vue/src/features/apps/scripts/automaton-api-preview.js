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
    const inPreviewPath = resolved.pathname.includes('/app-preview/');
    return sameOrigin && inPreviewPath ? resolved.toString() : null;
  } catch {
    return null;
  }
}

async function executePreviewAutomationAction(action, args = {}) {
  try {
    switch (action) {
      case 'ui_navigate': {
        const url = String(args.url || '').trim();
        if (!url) return { success: false, action, error: "Missing required argument 'url'" };
        const safeTarget = resolveSafePreviewNavigationTarget(url);
        if (!safeTarget) {
          return {
            success: false,
            action,
            error: 'Blocked ui_navigate outside preview app scope. Use relative app URLs only.'
          };
        }
        window.location.assign(safeTarget);
        return { success: true, action, details: `Navigated to ${safeTarget}` };
      }
      case 'ui_click': {
        const selector = String(args.selector || '').trim();
        if (!selector) return { success: false, action, error: "Missing required argument 'selector'" };
        const element = document.querySelector(selector);
        if (!element) return { success: false, action, error: `Element not found for selector: ${selector}` };
        element.click();
        return { success: true, action, details: `Clicked ${selector}` };
      }
      case 'ui_type': {
        const selector = String(args.selector || '').trim();
        const text = String(args.text || '');
        if (!selector) return { success: false, action, error: "Missing required argument 'selector'" };
        const element = document.querySelector(selector);
        if (!element || (element.tagName !== 'INPUT' && element.tagName !== 'TEXTAREA')) {
          return { success: false, action, error: `Input element not found for selector: ${selector}` };
        }
        element.focus();
        element.value = text;
        element.dispatchEvent(new Event('input', { bubbles: true }));
        element.dispatchEvent(new Event('change', { bubbles: true }));
        return { success: true, action, details: `Typed into ${selector}` };
      }
      case 'ui_read_text': {
        const selector = String(args.selector || '').trim();
        if (!selector) return { success: false, action, error: "Missing required argument 'selector'" };
        const element = document.querySelector(selector);
        if (!element) return { success: false, action, error: `Element not found for selector: ${selector}` };
        const value = (element.textContent || '').trim();
        return { success: true, action, value, details: `Read text from ${selector}` };
      }
      case 'ui_wait_for': {
        const selector = String(args.selector || '').trim();
        const timeoutMs = Number(args.timeoutMs || 3000);
        const intervalMs = Number(args.intervalMs || 100);
        if (!selector) return { success: false, action, error: "Missing required argument 'selector'" };
        const found = await waitForSelector(selector, timeoutMs, intervalMs);
        if (!found) {
          return { success: false, action, error: `Timed out waiting for selector: ${selector} (${timeoutMs}ms)` };
        }
        return { success: true, action, details: `Selector appeared: ${selector}` };
      }
      case 'ui_assert': {
        const selector = String(args.selector || '').trim();
        const exists = args.exists === undefined ? true : Boolean(args.exists);
        const containsText = args.containsText === undefined ? undefined : String(args.containsText);
        const equalsText = args.equalsText === undefined ? undefined : String(args.equalsText);
        if (!selector) return { success: false, action, error: "Missing required argument 'selector'" };

        const element = document.querySelector(selector);
        const found = Boolean(element);
        if (exists !== found) {
          return { success: false, action, error: `Assertion failed for ${selector}: expected exists=${exists}, actual=${found}` };
        }

        if (element && containsText !== undefined) {
          const text = (element.textContent || '').trim();
          if (!text.includes(containsText)) {
            return { success: false, action, error: `Assertion failed for ${selector}: text does not include "${containsText}"` };
          }
        }

        if (element && equalsText !== undefined) {
          const text = (element.textContent || '').trim();
          if (text !== equalsText) {
            return { success: false, action, error: `Assertion failed for ${selector}: expected "${equalsText}", got "${text}"` };
          }
        }

        return { success: true, action, details: `Assertion passed for ${selector}` };
      }
      case 'ui_get_page_snapshot': {
        const maxChars = Math.max(100, Number(args.maxChars || 1000));
        const title = document.title || '';
        const url = window.location.href || '';
        const bodyText = (document.body?.innerText || '').slice(0, maxChars);
        return { success: true, action, details: `Snapshot captured (${bodyText.length} chars)`, snapshot: { title, url, bodyText } };
      }
      default:
        return { success: false, action, error: `Unsupported UI action in preview bridge: ${action}` };
    }
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
