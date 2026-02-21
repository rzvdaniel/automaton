/**
 * Automaton Web App API - Client library for iframe-embedded apps
 * This script is injected into web app HTML and provides the automatonApi global object.
 * 
 * Usage: automatonApi.executeFlow('FlowName', { inputs: {...} })
 */

(function(window, APP_SLUG) {
  'use strict';

  console.log('[AutomatonAPI-IFRAME] Initializing for app:', APP_SLUG);

  function getAppSlug() {
    return APP_SLUG;
  }

  function generateRequestId() {
    return 'req_' + Math.random().toString(36).substring(2, 15);
  }

  function apiCall(path, method = 'GET', body = null) {
    return new Promise((resolve, reject) => {
      if (window === window.parent) {
        reject(new Error('This API can only be used within an Automaton web app iframe'));
        return;
      }

      const requestId = generateRequestId();
      const timeout = 30000;

      const payload = { id: requestId, path: path, method: method };
      if (body) {
        payload.body = typeof body === 'string' ? body : JSON.stringify(body);
      }

      console.log('[AutomatonAPI-IFRAME] Sending API request:', requestId, path);

      const timeoutId = setTimeout(() => {
        window.removeEventListener('message', responseHandler);
        reject(new Error('Request timeout after 30 seconds'));
      }, timeout);

      function responseHandler(event) {
        const { type, id, result } = event.data || {};
        if (type === 'apiResponse' && id === requestId) {
          console.log('[AutomatonAPI-IFRAME] Received response:', requestId);
          clearTimeout(timeoutId);
          window.removeEventListener('message', responseHandler);
          if (result.error) {
            reject(result);
          } else {
            resolve(result);
          }
        }
      }

      window.addEventListener('message', responseHandler);
      
      // Retry ping/pong handshake to ensure parent handler is ready
      let retries = 0;
      const maxRetries = 50; // 5 seconds max (50 * 100ms)
      
      function sendWhenReady() {
        window.parent.postMessage({ type: 'ping' }, '*');
        
        const pingTimeout = setTimeout(() => {
          retries++;
          if (retries < maxRetries) {
            sendWhenReady();
          } else {
            reject(new Error('Parent handler not ready after 5 seconds'));
          }
        }, 100);
        
        function pongHandler(e) {
          if (e.data?.type === 'pong') {
            clearTimeout(pingTimeout);
            window.removeEventListener('message', pongHandler);
            window.parent.postMessage({ type: 'apiRequest', payload: payload }, '*');
            console.log('[AutomatonAPI-IFRAME] Message posted to parent');
          }
        }
        window.addEventListener('message', pongHandler);
      }
      
      sendWhenReady();
    });
  }

  /**
   * Execute a flow by name or GUID
   * @param {string} flowIdentifier - Flow name or GUID
   * @param {object} options - Execution options
   * @param {object} options.inputs - Input variables as key-value pairs
   * @param {string[]} options.runnerNames - Optional array of runner names
   * @returns {Promise<object>} Flow execution result
   */
  function executeFlow(flowIdentifier, options = {}) {
    const appSlug = getAppSlug();
    if (!appSlug) {
      return Promise.reject(new Error('Could not determine app slug'));
    }
    const path = `/api/web-apps/${appSlug}/flows/${encodeURIComponent(flowIdentifier)}/execute`;
    const body = { inputs: options.inputs || {}, runnerNames: options.runnerNames || null };
    return apiCall(path, 'POST', body).then(response => response.data || response);
  }

  /**
   * Execute a Custom Step by name or GUID
   * @param {string} stepIdentifier - Step name or GUID
   * @param {object} options - Execution options
   * @param {object} options.inputs - Input variables as key-value pairs
   * @param {string[]} options.runnerNames - Optional array of runner names
   * @returns {Promise<object>} Step execution result
   */
  function executeStep(stepIdentifier, options = {}) {
    const appSlug = getAppSlug();
    if (!appSlug) {
      return Promise.reject(new Error('Could not determine app slug'));
    }
    
    const path = `/api/web-apps/${appSlug}/steps/${encodeURIComponent(stepIdentifier)}/execute`;
    const body = { 
      inputs: options.inputs || {}, 
      runnerNames: options.runnerNames || null 
    };
    
    return apiCall(path, 'POST', body).then(response => response.data || response);
  }

  /**
   * Execute a controller action by controller name and action name
   * @param {string} controllerName - Controller name (e.g. "Users")
   * @param {string} actionName - Action name (e.g. "create")
   * @param {object} options - Execution options
   * @param {object} options.inputs - Input variables as key-value pairs
   * @param {string[]} options.runnerNames - Optional array of runner names
   * @returns {Promise<object>} Controller action result with outputVariables
   */
  function executeController(controllerName, actionName, options = {}) {
    const appSlug = getAppSlug();
    if (!appSlug) {
      return Promise.reject(new Error('Could not determine app slug'));
    }
    const path = `/api/web-apps/${appSlug}/controllers/${encodeURIComponent(controllerName)}/actions/${encodeURIComponent(actionName)}/execute`;
    const body = {
      inputs: options.inputs || {},
      runnerNames: options.runnerNames || null
    };
    return apiCall(path, 'POST', body).then(response => response.data || response);
  }

  /**
   * Make a custom API call (advanced usage)
   * @param {string} path - Full API path (must start with /api/web-apps/)
   * @param {string} method - HTTP method (GET or POST)
   * @param {object|null} body - Request body
   * @returns {Promise<object>} API response
   */
  function customCall(path, method = 'GET', body = null) {
    if (!path.startsWith('/api/web-apps/')) {
      return Promise.reject(new Error('API path must start with /api/web-apps/'));
    }
    return apiCall(path, method, body).then(response => response.data || response);
  }

  // Expose public API
  window.automatonApi = { 
    executeFlow: executeFlow, 
    executeStep: executeStep,
    executeController: executeController,
    customCall: customCall, 
    getAppSlug: getAppSlug 
  };
  
  console.log('[AutomatonAPI-IFRAME] Ready! App slug:', APP_SLUG);

})(window, '{{APP_SLUG}}'); // APP_SLUG will be replaced by server
