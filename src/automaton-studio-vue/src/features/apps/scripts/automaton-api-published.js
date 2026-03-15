/**
 * automatonApi — injected into every published app iframe.
 *
 * Placeholder tokens are substituted at runtime by WebAppView.vue before
 * the script is written into the iframe document:
 *   __APP_SLUG__   → the app's public slug
 *   __BASE_URL__   → VITE_API_BASE_URL (e.g. https://localhost:7091)
 *   __AUTH_TOKEN__ → JWT bearer token, or empty string for anonymous apps
 */
'use strict';

// Runtime-injected configuration — DO NOT edit these literal values here
const APP_SLUG = '__APP_SLUG__';
const BASE_URL = '__BASE_URL__';
const AUTH_TOKEN = '__AUTH_TOKEN__'; // empty string = anonymous app

console.log('[AutomatonAPI] Published app loaded:', APP_SLUG);

async function apiCall(path, method, body) {
  const url = BASE_URL + path;
  console.log(`[AutomatonAPI] ${method} ${url}`);

  const headers = { 'Content-Type': 'application/json' };
  if (AUTH_TOKEN) {
    headers['Authorization'] = `Bearer ${AUTH_TOKEN}`;
  }

  const options = {
    method,
    headers,
    credentials: AUTH_TOKEN ? 'same-origin' : 'include'
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
  const path = `/api/web-apps/${encodeURIComponent(APP_SLUG)}/flows/${encodeURIComponent(flowIdentifier)}/execute`;
  return apiCall(path, 'POST', { inputs, runnerNames });
}

async function executeController(controllerName, actionName, { inputs = {}, runnerNames = null } = {}) {
  const path = `/api/web-apps/${encodeURIComponent(APP_SLUG)}/controllers/${encodeURIComponent(controllerName)}/actions/${encodeURIComponent(actionName)}/execute`;
  return apiCall(path, 'POST', { inputs, runnerNames });
}

async function callController(controllerName, actionName, inputs = {}) {
  const res = await executeController(controllerName, actionName, { inputs });
  if (!res.success) throw new Error(res.error || 'Request failed');
  return res.data;
}

window.automatonApi = {
  executeFlow,
  callController,
  getAppId: () => null,
  getAppSlug: () => APP_SLUG,
  getBaseUrl: () => BASE_URL
};

console.log('[AutomatonAPI] Ready!');
