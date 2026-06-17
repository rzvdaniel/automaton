/**
 * automaton-dev.js — Local development polyfill for window.automatonApi
 *
 * Provides a mock implementation that reads from backend/mocks/ JSON files so
 * apps can be tested locally without a running Automaton server or runner.
 *
 * SAFE TO KEEP IN index.html: the `if (!window.automatonApi)` guard makes it a
 * no-op when Automaton injects its real implementation at serve time.
 *
 * Mock file convention:
 *   backend/mocks/{controllerName}.{actionName}.json
 *
 * Example for callController('scores', 'getTop', { limit: 10 }):
 *   backend/mocks/scores.getTop.json → { "success": true, "data": { "scores": [] } }
 */
(function () {
  'use strict';

  if (window.automatonApi) {
    // Platform already injected the real implementation — nothing to do.
    return;
  }

  let _backendMode = 'mock';

  async function callController(controllerName, actionName, inputs) {
    const path = 'backend/mocks/' + controllerName + '.' + actionName + '.json';
    try {
      const response = await fetch(path);
      if (!response.ok) {
        console.warn(
          '[AutomatonAPI] Mock file not found: ' + path + '\n' +
          'Create this file to test locally. Example content:\n' +
          '{ "success": true, "data": {} }'
        );
        return null;
      }
      const mock = await response.json();
      console.log('[AutomatonAPI] Mock response for ' + controllerName + '.' + actionName + ':', mock);
      return mock.data ?? mock;
    } catch (err) {
      console.error('[AutomatonAPI] Failed to load mock file: ' + path, err);
      return null;
    }
  }

  async function executeFlow(flowId, options) {
    console.warn('[AutomatonAPI] executeFlow() is not available in local dev mode. Returning stub response.');
    return { success: true, status: 'Completed', data: {} };
  }

  async function uploadFile(file) {
    console.warn('[AutomatonAPI] uploadFile() is not available in local dev mode. Returning stub session_id.');
    return { session_id: 'dev-' + Date.now() };
  }

  async function downloadFile(sessionId, filename) {
    console.warn('[AutomatonAPI] downloadFile() is not available in local dev mode.');
  }

  window.automatonApi = {
    callController: callController,
    executeFlow: executeFlow,
    uploadFile: uploadFile,
    downloadFile: downloadFile,
    getBackendMode: function () { return _backendMode; },
    setBackendMode: function (mode) { _backendMode = mode; return mode; },
    getAppId: function () { return null; },
    getAppSlug: function () { return null; },
    getBaseUrl: function () { return null; }
  };

  console.log('[AutomatonAPI] Dev polyfill loaded. Reading mock responses from backend/mocks/');
})();
