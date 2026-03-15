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

window.automatonApi = {
  executeFlow,
  executeController,
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
