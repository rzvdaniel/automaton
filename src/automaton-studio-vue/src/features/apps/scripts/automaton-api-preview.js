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

console.log('[AutomatonAPI] Preview mode initialized for app:', APP_ID);

async function apiCall(path, method, body) {
  const url = BASE_URL + path;
  console.log(`[AutomatonAPI] ${method} ${url}`);

  const options = {
    method,
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${AUTH_TOKEN}`
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
    const result = await apiCall(path, 'POST', { inputs, runnerNames });
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
    const result = await apiCall(path, 'POST', { inputs, runnerNames });
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
  getBaseUrl: () => BASE_URL
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

console.log('[AutomatonAPI] Ready!');
