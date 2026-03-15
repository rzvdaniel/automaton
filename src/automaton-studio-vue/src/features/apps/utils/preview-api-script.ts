/**
 * Builds the automatonApi injection script for preview mode.
 * Injected into index.html before the app's own scripts load.
 */
export function buildPreviewApiScript(appId: string, baseUrl: string, token: string): string {
  return `
<script id="__automaton_api__">
(function(window, APP_ID, BASE_URL, AUTH_TOKEN) {
  'use strict';

  console.log('[AutomatonAPI] Preview mode initialized for app:', APP_ID);

  async function apiCall(path, method, body) {
    const url = BASE_URL + path;
    console.log('[AutomatonAPI] ' + method + ' ' + url);

    const options = {
      method: method,
      headers: {
        'Content-Type': 'application/json',
        'Authorization': 'Bearer ' + AUTH_TOKEN
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

  async function executeFlow(flowIdentifier, options) {
    options = options || {};
    var inputs = options.inputs || {};
    var startTime = Date.now();
    var path = '/api/web-apps/preview/' + APP_ID + '/flows/' + encodeURIComponent(flowIdentifier) + '/execute';
    var body = { inputs: inputs, runnerNames: options.runnerNames || null };

    try {
      var result = await apiCall(path, 'POST', body);
      sendResultToConsole('flow', flowIdentifier, inputs, result, Date.now() - startTime, true);
      return result;
    } catch (error) {
      sendResultToConsole('flow', flowIdentifier, inputs, { success: false, status: 'Error', error: error.message }, Date.now() - startTime, false);
      throw error;
    }
  }

  async function executeController(controllerName, actionName, options) {
    options = options || {};
    var inputs = options.inputs || {};
    var startTime = Date.now();
    var path = '/api/web-apps/preview/' + APP_ID + '/controllers/' + encodeURIComponent(controllerName) + '/actions/' + encodeURIComponent(actionName) + '/execute';
    var body = { inputs: inputs, runnerNames: options.runnerNames || null };

    try {
      var result = await apiCall(path, 'POST', body);
      sendResultToConsole('controller', controllerName + '.' + actionName, inputs, result, Date.now() - startTime, true);
      return result;
    } catch (error) {
      sendResultToConsole('controller', controllerName + '.' + actionName, inputs, { success: false, status: 'Error', error: error.message }, Date.now() - startTime, false);
      throw error;
    }
  }

  function sendResultToConsole(type, name, inputs, result, duration, success) {
    try {
      window.parent.postMessage({
        type: 'automatonStepResult',
        data: {
          stepName: name,
          inputs: inputs,
          result: result,
          duration: duration,
          timestamp: new Date().toISOString(),
          source: 'preview'
        }
      }, '*');
    } catch (e) {
      console.warn('[AutomatonAPI] Failed to send result to console:', e);
    }
  }

  window.automatonApi = {
    executeFlow: executeFlow,
    executeController: executeController,
    getAppId: function() { return APP_ID; },
    getAppSlug: function() { return null; },
    getBaseUrl: function() { return BASE_URL; }
  };

  window.addEventListener('error', function(event) {
    try {
      var target = event.target || event.srcElement;
      if (!target) return;

      var url = target.src || target.href;
      if (!url) return;

      window.parent.postMessage({
        type: 'previewResourceError',
        data: {
          url: url,
          tagName: (target.tagName || '').toLowerCase()
        }
      }, '*');
    } catch (e) {
      // Ignore preview error reporting failures
    }
  }, true);

  console.log('[AutomatonAPI] Ready!');

})(window, '${appId}', '${baseUrl}', '${token}');
<\/script>`
}
