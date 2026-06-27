(function () {
  const config = window.__AUTOMATON_APP__ || {};
  const appId = String(config.appId || '').trim();
  const apiBaseUrl = String(config.apiBaseUrl || '').replace(/\/+$/, '');
  const backendApiPath = String(config.backendApiPath || '').trim();
  const runtimeToken = String(config.runtimeToken || '').trim();

  if (!backendApiPath) {
    console.warn('Automaton app backend API path is not configured.');
  }

  function getAppApiRoot() {
    if (!apiBaseUrl) {
      throw new Error('Automaton API base URL is not configured.');
    }

    if (!backendApiPath) {
      throw new Error('Automaton app backend API path is not configured.');
    }

    return `${apiBaseUrl}${backendApiPath}`;
  }

  async function apiCall(path, method, body) {
    const headers = {};
    if (body) {
      headers['Content-Type'] = 'application/json';
    }
    if (runtimeToken) {
      headers['Authorization'] = `Bearer ${runtimeToken}`;
    }

    const response = await fetch(`${getAppApiRoot()}${path}`, {
      method,
      headers: Object.keys(headers).length > 0 ? headers : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });

    const contentType = response.headers.get('content-type') || '';
    const payload = contentType.includes('application/json')
      ? await response.json()
      : await response.text();

    if (!response.ok) {
      const message = typeof payload === 'string'
        ? payload
        : payload?.message || payload?.error || `Automaton API request failed (${response.status})`;
      throw new Error(message);
    }

    return payload;
  }

  async function executeFlow(flowIdentifier, { inputs = {}, runnerNames = null } = {}) {
    return apiCall(
      `/flows/${encodeURIComponent(flowIdentifier)}/execute`,
      'POST',
      { inputs, runnerNames },
    );
  }

  async function executeController(controllerName, actionName, { inputs = {}, runnerNames = null } = {}) {
    return apiCall(
      `/controllers/${encodeURIComponent(controllerName)}/actions/${encodeURIComponent(actionName)}/execute`,
      'POST',
      { inputs, runnerNames },
    );
  }

  async function callController(controllerName, actionName, inputs = {}) {
    const res = await executeController(controllerName, actionName, { inputs });
    if (!res.success) {
      throw new Error(res.error || 'Request failed');
    }

    return res.data;
  }

  async function uploadFile(file, { runnerNames = null } = {}) {
    const formData = new FormData();
    formData.append('file', file);
    if (runnerNames) {
      formData.append('runnerNames', JSON.stringify(runnerNames));
    }

    const response = await fetch(`${getAppApiRoot()}/files/upload`, {
      method: 'POST',
      headers: runtimeToken ? { 'Authorization': `Bearer ${runtimeToken}` } : undefined,
      body: formData,
    });

    if (!response.ok) {
      throw new Error(`File upload failed (${response.status})`);
    }

    return response.json();
  }

  function getDownloadUrl(sessionId) {
    return `${getAppApiRoot()}/files/${encodeURIComponent(sessionId)}`;
  }

  async function downloadFile(sessionId, filename) {
    const response = await fetch(getDownloadUrl(sessionId), {
      headers: runtimeToken ? { 'Authorization': `Bearer ${runtimeToken}` } : undefined,
    });

    if (!response.ok) {
      throw new Error(`Download failed: ${response.status}`);
    }

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
    config,
    executeFlow,
    callController,
    uploadFile,
    downloadFile,
    getDownloadUrl,
    getAppId: () => appId || null,
    getBaseUrl: () => apiBaseUrl,
    getBackendApiPath: () => backendApiPath || null,
  };
})();
