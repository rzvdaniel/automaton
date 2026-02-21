/**
 * Apps service for API calls
 * Connects to ASP.NET backend apps endpoints
 */
import apiClient from '@/shared/services/api/axios-instance'
import type {
  App,
  CreateAppRequest,
  PublishAppRequest,
  LinkedStepInfo,
  AppFile,
  FileNode,
  CreateAppStepRequest,
} from '../types/apps.types'
import { getFileIcon } from '../types/apps.types'

class AppsService {
  /**
   * Get all apps for the current user
   * GET /api/apps
   */
  async list(projectId?: string): Promise<App[]> {
    const params = projectId ? { projectId } : {}
    const { data } = await apiClient.get<App[]>('/api/apps', { params })
    return data
  }

  /**
   * Get a single app by ID
   * GET /api/apps/{id}
   */
  async get(id: string): Promise<App> {
    const { data } = await apiClient.get<App>(`/api/apps/${id}`)
    return data
  }

  /**
   * Create a new app
   * POST /api/apps
   */
  async create(request: CreateAppRequest): Promise<App> {
    const { data } = await apiClient.post<App>('/api/apps', request)
    return data
  }

  /**
   * Update an existing app
   * PUT /api/apps/{id}
   */
  async update(id: string, app: Partial<App>): Promise<void> {
    await apiClient.put(`/api/apps/${id}`, app)
  }

  /**
   * Delete an app
   * DELETE /api/apps/{id}
   */
  async delete(id: string): Promise<void> {
    await apiClient.delete(`/api/apps/${id}`)
  }

  /**
   * Publish an app
   * POST /api/apps/{id}/publish
   */
  async publish(id: string, request: PublishAppRequest): Promise<void> {
    await apiClient.post(`/api/apps/${id}/publish`, request)
  }

  /**
   * Unpublish an app
   * POST /api/apps/{id}/unpublish
   */
  async unpublish(id: string): Promise<void> {
    await apiClient.post(`/api/apps/${id}/unpublish`)
  }

  /**
   * Deploy backend scripts to the app's configured runner(s).
   * POST /api/apps/{id}/backend/deploy
   */
  async deployBackend(id: string): Promise<{ success: boolean; deployedCount: number; totalScripts: number; runnerCount: number; message: string }> {
    const { data } = await apiClient.post(`/api/apps/${id}/backend/deploy`, {})
    return data
  }

  /**
   * Update the execution runner for an app
   * PUT /api/apps/{id}/runner
   */
  async updateRunner(id: string, runnerId: string | null): Promise<void> {
    await apiClient.put(`/api/apps/${id}/runner`, { executionRunnerId: runnerId })
  }

  /**
   * Update app settings (name, description, publicUrl, etc.)
   * Uses the general PUT /api/apps/{id} endpoint
   */
  async updateSettings(
    id: string,
    settings: {
      name?: string
      summary?: string
      description?: string
      publicUrl?: string
      requiresAuthentication?: boolean
      executionRunnerId?: string
    },
  ): Promise<void> {
    // Get current app and build update payload with only backend-expected fields
    const app = await this.get(id)
    const updatePayload = {
      id: app.id,
      name: settings.name ?? app.name,
      summary: settings.summary ?? app.summary ?? '',
      description: settings.description ?? app.description,
      isPublished: app.isPublished,
      publicUrl: settings.publicUrl ?? app.publicUrl,
      publishedAt: app.publishedAt,
      version: app.version,
      requiresAuthentication: settings.requiresAuthentication ?? app.requiresAuthentication,
      created: app.created,
      updated: app.updated,
      executionRunnerId: settings.executionRunnerId ?? app.executionRunnerId,
      definition: app.definition,
      thumbnailUrl: app.thumbnailUrl,
      category: app.category,
      tags: app.tags,
    }
    await this.update(id, updatePayload)
  }

  /**
   * Upload app thumbnail/background image
   * POST /api/apps/{id}/upload-background
   */
  async uploadThumbnail(id: string, file: File): Promise<string> {
    const formData = new FormData()
    formData.append('file', file)

    const { data } = await apiClient.post<{ imageUrl: string }>(
      `/api/apps/${id}/upload-background`,
      formData,
      {
        headers: {
          'Content-Type': 'multipart/form-data',
        },
      },
    )
    return data.imageUrl
  }

  /**
   * Remove app thumbnail image by clearing the thumbnailUrl
   * Uses the general PUT /api/apps/{id} endpoint
   */
  async removeThumbnail(id: string): Promise<void> {
    const app = await this.get(id)
    const updatePayload = {
      id: app.id,
      name: app.name,
      summary: app.summary ?? '',
      description: app.description,
      isPublished: app.isPublished,
      publicUrl: app.publicUrl,
      publishedAt: app.publishedAt,
      version: app.version,
      requiresAuthentication: app.requiresAuthentication,
      created: app.created,
      updated: app.updated,
      executionRunnerId: app.executionRunnerId,
      definition: app.definition,
      thumbnailUrl: null,
      category: app.category,
      tags: app.tags,
    }
    await this.update(id, updatePayload)
  }

  /**
   * Get linked steps for an app
   * GET /api/apps/{id}/steps
   */
  async getLinkedSteps(id: string): Promise<LinkedStepInfo[]> {
    const { data } = await apiClient.get<LinkedStepInfo[]>(`/api/apps/${id}/steps`)
    return data
  }

  /**
   * Get available steps that can be linked to an app
   * GET /api/apps/{id}/steps/available
   */
  async getAvailableSteps(id: string): Promise<LinkedStepInfo[]> {
    const { data } = await apiClient.get<LinkedStepInfo[]>(`/api/apps/${id}/steps/available`)
    return data
  }

  /**
   * Get files for an app
   * GET /api/apps/{id}/files
   */
  async getFiles(id: string): Promise<{ success: boolean; files: AppFile[] }> {
    const { data } = await apiClient.get<{ success: boolean; files: AppFile[] }>(
      `/api/apps/${id}/files`,
    )
    return data
  }

  /**
   * Read a file content
   * GET /api/apps/{id}/files/{path}
   */
  async readFile(id: string, path: string): Promise<{ success: boolean; content: string }> {
    const { data } = await apiClient.get<{ success: boolean; content: string }>(
      `/api/apps/${id}/files/${path}`,
    )
    return data
  }

  /**
   * Write file content
   * PUT /api/apps/{id}/files/{path}
   */
  async writeFile(id: string, path: string, content: string): Promise<void> {
    await apiClient.put(`/api/apps/${id}/files/${path}`, { content })
  }

  /**
   * Delete a file
   * DELETE /api/apps/{id}/files/{path}
   */
  async deleteFile(id: string, path: string): Promise<void> {
    await apiClient.delete(`/api/apps/${id}/files/${path}`)
  }

  /**
   * Create a new file
   * PUT /api/apps/{id}/files/{path}
   */
  async createFile(id: string, path: string, content: string = ''): Promise<void> {
    await apiClient.put(`/api/apps/${id}/files/${path}`, { content })
  }

  /**
   * Build file tree from flat list of file paths
   */
  buildFileTree(files: string[]): FileNode[] {
    const root: FileNode[] = []
    const folderMap = new Map<string, FileNode>()

    // Filter out metadata file and normalize paths
    const normalizedFiles = files
      .filter((f) => f !== '_metadata.json')
      .map((f) => f.replace(/\\/g, '/'))
      .sort()

    for (const filePath of normalizedFiles) {
      const parts = filePath.split('/')
      let currentLevel = root
      let currentPath = ''

      for (let i = 0; i < parts.length; i++) {
        const part = parts[i]!
        currentPath = currentPath ? `${currentPath}/${part}` : part
        const isFile = i === parts.length - 1

        if (isFile) {
          // Add file node
          const ext = part.split('.').pop() || ''
          currentLevel.push({
            key: currentPath,
            name: part,
            path: currentPath,
            type: 'file',
            extension: ext,
            icon: getFileIcon(part),
          })
        } else {
          // Add or get folder node
          if (!folderMap.has(currentPath)) {
            const folderNode: FileNode = {
              key: currentPath,
              name: part,
              path: currentPath,
              type: 'folder',
              icon: 'pi pi-folder',
              children: [],
              isOpen: true,
            }
            currentLevel.push(folderNode)
            folderMap.set(currentPath, folderNode)
          }
          currentLevel = folderMap.get(currentPath)!.children!
        }
      }
    }

    return root
  }

  /**
   * Get file tree structure
   * GET /api/apps/{id}/files then build tree
   */
  async getFileTree(id: string): Promise<FileNode[]> {
    const response = await this.getFiles(id)
    if (response.success && response.files) {
      // The API returns file paths as strings
      const filePaths = response.files
        .map((f) => (typeof f === 'string' ? f : (f as AppFile).path))
        .filter((p): p is string => Boolean(p))
      return this.buildFileTree(filePaths)
    }
    return []
  }

  /**
   * Create a step and link it to the app
   * POST /api/apps/{appId}/steps/create
   */
  async createStepForApp(appId: string, request: CreateAppStepRequest): Promise<LinkedStepInfo> {
    const { data } = await apiClient.post<LinkedStepInfo>(
      `/api/apps/${appId}/steps/create`,
      request,
    )
    return data
  }

  /**
   * Link an existing step to an app
   * POST /api/apps/{appId}/steps/{stepId}/link
   */
  async linkStep(appId: string, stepId: string): Promise<void> {
    await apiClient.post(`/api/apps/${appId}/steps/${stepId}/link`)
  }

  /**
   * Unlink a step from an app
   * DELETE /api/apps/{appId}/steps/{stepId}
   */
  async unlinkStep(appId: string, stepId: string): Promise<void> {
    await apiClient.delete(`/api/apps/${appId}/steps/${stepId}`)
  }

  /**
   * Get step code for editing
   * GET /api/customsteps/{stepId}
   */
  async getStepCode(stepId: string): Promise<{ code: string; language: string }> {
    const { data } = await apiClient.get<{ definition: { code: string }; language: string }>(
      `/api/customsteps/${stepId}`,
    )
    return {
      code: data.definition?.code || '',
      language: data.language || 'python',
    }
  }

  /**
   * Update step code
   * PUT /api/customsteps/{stepId}
   */
  async updateStepCode(stepId: string, code: string): Promise<void> {
    // First get the step to preserve other fields
    const { data: step } = await apiClient.get<{
      id: string
      name: string
      displayName: string
      description: string
      language: string
      definition: { code: string }
    }>(`/api/customsteps/${stepId}`)

    // Update only the code
    await apiClient.put(`/api/customsteps/${stepId}`, {
      ...step,
      definition: {
        ...step.definition,
        code,
      },
    })
  }

  /**
   * Get preview HTML for an app (with automatonApi.js injected)
   * Returns the app HTML with the Automaton API script injected
   * The preview makes direct fetch calls to the backend with the auth token
   */
  async getPreviewHtml(id: string): Promise<string> {
    const app = await this.get(id)
    let html = app.definition?.htmlContent || this.getDefaultHtml()

    // Get current auth token and base URL for injection
    const token = localStorage.getItem('automaton_access_token') || ''
    const baseUrl = import.meta.env.VITE_API_BASE_URL || ''

    // Inject preview API that makes direct fetch calls
    const previewScript = `
<script>
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

  async function executeStep(stepIdentifier, options) {
    options = options || {};
    var inputs = options.inputs || {};
    var startTime = Date.now();
    var path = '/api/web-apps/preview/' + APP_ID + '/steps/' + encodeURIComponent(stepIdentifier) + '/execute';
    var body = { inputs: inputs, runnerNames: options.runnerNames || null };

    try {
      var result = await apiCall(path, 'POST', body);
      sendResultToConsole('step', stepIdentifier, inputs, result, Date.now() - startTime, true);
      return result;
    } catch (error) {
      sendResultToConsole('step', stepIdentifier, inputs, { success: false, status: 'Error', error: error.message }, Date.now() - startTime, false);
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
    executeStep: executeStep,
    getAppId: function() { return APP_ID; },
    getAppSlug: function() { return null; }
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

})(window, '${id}', '${baseUrl}', '${token}');
</script>`

    // Inject base tag for resolving relative paths to app files in preview
    const appBaseUrl = `${baseUrl}/app-preview/${app.userId}/${id}/`
    const baseTag = `<base href="${appBaseUrl}">`

    // Add cache-busting to CSS and JS references to ensure changes are immediately visible
    const cacheBuster = Date.now()
    html = html.replace(
      /(<link[^>]+href=["'])([^"'?]+\.css)(["'])/gi,
      `$1$2?v=${cacheBuster}$3`
    )
    html = html.replace(
      /(<script[^>]+src=["'])([^"'?]+\.js)(["'])/gi,
      `$1$2?v=${cacheBuster}$3`
    )

    // Inject both base tag and script at the start of <head> if exists, otherwise prepend
    if (html.includes('<head>')) {
      const headIndex = html.indexOf('<head>') + 6
      return html.slice(0, headIndex) + baseTag + previewScript + html.slice(headIndex)
    } else if (html.includes('<body')) {
      const bodyMatch = html.match(/<body[^>]*>/)
      if (bodyMatch) {
        const bodyIndex = html.indexOf(bodyMatch[0]) + bodyMatch[0].length
        return html.slice(0, bodyIndex) + baseTag + previewScript + html.slice(bodyIndex)
      }
    }

    return baseTag + previewScript + html
  }

  /**
   * Get default HTML template for new apps
   */
  getDefaultHtml(): string {
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>My App</title>
    <style>
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            margin: 0;
            padding: 20px;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
        }
        .container {
            background: white;
            padding: 40px;
            border-radius: 10px;
            box-shadow: 0 10px 40px rgba(0,0,0,0.2);
            max-width: 600px;
            width: 100%;
        }
        h1 { color: #333; margin-top: 0; }
        p { color: #666; line-height: 1.6; }
        .button {
            background: #667eea;
            color: white;
            border: none;
            padding: 12px 24px;
            border-radius: 5px;
            cursor: pointer;
            font-size: 16px;
            margin-top: 20px;
        }
        .button:hover { background: #5568d3; }
    </style>
</head>
<body>
    <div class="container">
        <h1>Welcome to Your App!</h1>
        <p>This is your app's starting template. Use the editor to customize it.</p>
        <button class="button" onclick="alert('Hello!')">Click Me</button>
    </div>
    <script>console.log('App loaded!');</script>
</body>
</html>`
  }

  /**
   * Get version history for an app with pagination
   * GET /api/apps/{appId}/versions
   */
  async getVersions(appId: string, pageNumber: number = 1, pageSize: number = 20) {
    const { data } = await apiClient.get(`/api/apps/${appId}/versions`, {
      params: { pageNumber, pageSize },
    })
    return data
  }

  /**
   * Get detailed information about a specific version
   * GET /api/apps/{appId}/versions/{versionNumber}
   */
  async getVersionDetail(appId: string, versionNumber: number) {
    const { data } = await apiClient.get(`/api/apps/${appId}/versions/${versionNumber}`)
    return data
  }

  /**
   * Create a manual version snapshot for an app
   * POST /api/apps/{appId}/versions/snapshot
   */
  async createVersionSnapshot(appId: string, trigger?: string) {
    const { data } = await apiClient.post(`/api/apps/${appId}/versions/snapshot`, {
      trigger: trigger || 'ManualSave',
    })
    return data
  }

  /**
   * Restore an app to a previous version
   * POST /api/apps/{appId}/versions/{versionNumber}/restore
   */
  async restoreVersion(appId: string, versionNumber: number) {
    const { data } = await apiClient.post(
      `/api/apps/${appId}/versions/${versionNumber}/restore`,
      {}
    )
    return data
  }

  /**
   * Upload preview content to the server for HTTP-based preview
   * POST /api/apps/{id}/preview
   */
  async uploadPreviewContent(id: string, html: string, files?: Record<string, string>): Promise<void> {
    await apiClient.post(`/api/apps/${id}/preview`, { html, files })
  }

  /**
   * Generate slug from app name
   */
  generateSlug(name: string): string {
    return name
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-|-$/g, '')
  }
}

export const appsService = new AppsService()
