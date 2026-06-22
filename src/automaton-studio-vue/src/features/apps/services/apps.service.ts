/**
 * Apps service for API calls
 * Connects to ASP.NET backend apps endpoints
 */
import apiClient, { tokenStorage } from '@/shared/services/api/axios-instance'
import type {
  App,
  AppScreenshot,
  CreateAppRequest,
  PublishAppRequest,
  ExportAppResponse,
  LinkedStepInfo,
  AppFile,
  AppFileSystemScope,
  ScopedFileContentResponse,
  ScopedFilesResponse,
  FileNode,
  CreateAppStepRequest,
  RunnerBackendFileContent,
  RunnerBackendFilesResponse,
  RunnerFileArea,
  ForkAppRequest,
  ForkAppSource,
} from '../types/apps.types'
import { getFileIcon } from '../types/apps.types'
import { buildPreviewApiScript } from '../utils/preview-api-script'

class AppsService {
  private sortFileNodes(nodes: FileNode[]): FileNode[] {
    return [...nodes]
      .map((node) => ({
        ...node,
        children: node.children ? this.sortFileNodes(node.children) : node.children,
      }))
      .sort((a, b) => {
        if (a.type !== b.type) {
          return a.type === 'folder' ? -1 : 1
        }

        return a.name.localeCompare(b.name, undefined, {
          sensitivity: 'base',
          numeric: true,
        })
      })
  }

  private sanitizeArchiveFileName(name: string | undefined, extension: string): string {
    const fallbackBaseName = 'app-export'
    const baseName = (name || '')
      .replace(/[<>:"/\\|?*\x00-\x1F]/g, '-')
      .trim()

    const finalBaseName = baseName || fallbackBaseName
    return finalBaseName.toLowerCase().endsWith(extension.toLowerCase())
      ? finalBaseName
      : `${finalBaseName}${extension}`
  }

  private getDownloadFileName(
    contentDisposition: string | undefined,
    fallbackName: string | undefined,
    extension: string,
  ): string {
    if (contentDisposition) {
      const utf8Match = contentDisposition.match(/filename\*\s*=\s*UTF-8''([^;]+)/i)
      if (utf8Match?.[1]) {
        return decodeURIComponent(utf8Match[1].trim().replace(/^"(.*)"$/, '$1'))
      }

      const asciiMatch = contentDisposition.match(/filename\s*=\s*("?)([^";]+)\1/i)
      if (asciiMatch?.[2]) {
        return asciiMatch[2].trim()
      }
    }

    return this.sanitizeArchiveFileName(fallbackName, extension)
  }

  private encodePath(path: string): string {
    return path.split('/').map(encodeURIComponent).join('/')
  }

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
   * Export an app as a portable zip archive.
   * GET /api/apps/{id}/export
   */
  async exportApp(id: string, fallbackAppName?: string): Promise<ExportAppResponse> {
    const response = await apiClient.get<Blob>(`/api/apps/${id}/export`, {
      responseType: 'blob',
    })

    const contentDisposition = response.headers['content-disposition'] as string | undefined
    const fileName = this.getDownloadFileName(contentDisposition, fallbackAppName, '.zip')

    return {
      blob: response.data,
      fileName,
    }
  }

  /**
   * Import an exported app bundle into the current user's account.
   * POST /api/apps/import
   */
  async importApp(zipFile: File): Promise<App> {
    const form = new FormData()
    form.append('file', zipFile)

    const { data } = await apiClient.post<App>('/api/apps/import', form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })

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
   * Returns a lightweight pre-deploy summary (script count, whether requirements.txt is present).
   * GET /api/apps/{id}/backend/deploy-info
   */
  async getDeployInfo(id: string): Promise<{ scriptCount: number; hasRequirements: boolean }> {
    const { data } = await apiClient.get(`/api/apps/${id}/backend/deploy-info`)
    return data
  }

  /**
   * Deploy backend scripts to the app's configured runner(s).
   * POST /api/apps/{id}/backend/deploy
   */
  async deployBackend(id: string): Promise<{ success: boolean; deployedCount: number; totalScripts: number; runnerCount: number; hasRequirements: boolean; message: string; installLogs?: string[] }> {
    const { data } = await apiClient.post(`/api/apps/${id}/backend/deploy`, {})
    return data
  }

  async deployApp(id: string): Promise<{
    success: boolean
    frontendFilesSynced: number
    backendFilesSynced: number
    frontendAction: string
    backendAction: string
    backendDeploy: { success: boolean; deployedCount: number; totalScripts: number; runnerCount: number; hasRequirements: boolean; message: string; installLogs?: string[] }
    message?: string
  }> {
    const { data } = await apiClient.post(`/api/apps/${id}/deploy`, {})
    return data
  }

  async deployFrontend(id: string): Promise<{
    success: boolean
    frontendFilesSynced: number
    backendFilesSynced: number
    frontendAction: string
    backendAction: string
    message?: string
  }> {
    const { data } = await apiClient.post(`/api/apps/${id}/frontend/deploy`, {})
    return data
  }

  async redeployBackendComponent(id: string): Promise<{
    success: boolean
    frontendFilesSynced: number
    backendFilesSynced: number
    frontendAction: string
    backendAction: string
    backendDeploy: { success: boolean; deployedCount: number; totalScripts: number; runnerCount: number; hasRequirements: boolean; message: string; installLogs?: string[] }
    message?: string
  }> {
    const { data } = await apiClient.post(`/api/apps/${id}/backend/redeploy`, {})
    return data
  }

  async createPrivateLaunch(id: string): Promise<{ launchCode: string }> {
    const { data } = await apiClient.post<{ launchCode: string }>(`/api/apps/${id}/private-launch`, {})
    return data
  }

  async listRunnerBackendFiles(id: string, area: RunnerFileArea): Promise<RunnerBackendFilesResponse> {
    const { data } = await apiClient.get<RunnerBackendFilesResponse>(
      `/api/apps/${id}/runner-backend/files`,
      { params: { area } },
    )
    return data
  }

  async readRunnerBackendFile(id: string, path: string, area: RunnerFileArea): Promise<RunnerBackendFileContent> {
    const { data } = await apiClient.get<RunnerBackendFileContent>(
      `/api/apps/${id}/runner-backend/files/${this.encodePath(path)}`,
      { params: { area } },
    )
    return data
  }

  async deleteRunnerBackendFile(id: string, path: string, area: RunnerFileArea): Promise<void> {
    await apiClient.delete(`/api/apps/${id}/runner-backend/files/${this.encodePath(path)}`, {
      params: { area },
    })
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
      isProfileFeatured?: boolean
      isForkable?: boolean
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
      isForkable: settings.isForkable ?? app.isForkable,
      publicUrl: settings.publicUrl ?? app.publicUrl,
      publishedAt: app.publishedAt,
      version: app.version,
      requiresAuthentication: settings.requiresAuthentication ?? app.requiresAuthentication,
      created: app.created,
      updated: app.updated,
      executionRunnerId: settings.executionRunnerId ?? app.executionRunnerId,
      isProfileFeatured: settings.isProfileFeatured ?? app.isProfileFeatured,
      definition: app.definition,
      thumbnailUrl: app.thumbnailUrl,
      screenshots: app.screenshots,
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
      isForkable: app.isForkable,
      publicUrl: app.publicUrl,
      publishedAt: app.publishedAt,
      version: app.version,
      requiresAuthentication: app.requiresAuthentication,
      created: app.created,
      updated: app.updated,
      executionRunnerId: app.executionRunnerId,
      isProfileFeatured: app.isProfileFeatured,
      definition: app.definition,
      thumbnailUrl: null,
      screenshots: app.screenshots,
      category: app.category,
      tags: app.tags,
    }
    await this.update(id, updatePayload)
  }

  async uploadScreenshot(id: string, file: File): Promise<AppScreenshot> {
    const formData = new FormData()
    formData.append('file', file)

    const { data } = await apiClient.post<AppScreenshot>(
      `/api/apps/${id}/screenshots`,
      formData,
      {
        headers: {
          'Content-Type': 'multipart/form-data',
        },
      },
    )

    return data
  }

  async deleteScreenshot(id: string, screenshotId: string): Promise<void> {
    await apiClient.delete(`/api/apps/${id}/screenshots/${screenshotId}`)
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

  async getScopedFiles(id: string, scope: AppFileSystemScope): Promise<ScopedFilesResponse> {
    const { data } = await apiClient.get<ScopedFilesResponse>(`/api/apps/${id}/filesystem`, {
      params: { scope },
    })
    return data
  }

  async getRootFiles(id: string): Promise<ScopedFilesResponse> {
    const { data } = await apiClient.get<ScopedFilesResponse>(`/api/apps/${id}/filesystem/root`)
    return data
  }

  async readRootFile(
    id: string,
    path: string,
  ): Promise<{ success: boolean; editable: boolean; content: string }> {
    const encodedPath = this.encodePath(path)
    const { data } = await apiClient.get<{ success: boolean; editable: boolean; content: string }>(
      `/api/apps/${id}/filesystem/root/${encodedPath}`,
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

  async readScopedFile(
    id: string,
    scope: AppFileSystemScope,
    path: string,
  ): Promise<ScopedFileContentResponse> {
    const encodedPath = this.encodePath(path)
    const { data } = await apiClient.get<ScopedFileContentResponse>(
      `/api/apps/${id}/filesystem/${scope}/${encodedPath}`,
    )
    return data
  }

  async writeScopedFile(
    id: string,
    scope: AppFileSystemScope,
    path: string,
    content: string,
  ): Promise<void> {
    const encodedPath = this.encodePath(path)
    await apiClient.put(`/api/apps/${id}/filesystem/${scope}/${encodedPath}`, { content })
  }

  async deleteScopedFile(id: string, scope: AppFileSystemScope, path: string): Promise<void> {
    const encodedPath = this.encodePath(path)
    await apiClient.delete(`/api/apps/${id}/filesystem/${scope}/${encodedPath}`)
  }

  async deleteScopedDirectory(id: string, scope: AppFileSystemScope, path: string): Promise<void> {
    const encodedPath = this.encodePath(path)
    await apiClient.delete(`/api/apps/${id}/filesystem/${scope}/directory/${encodedPath}`)
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
   * Upload a zip archive of app files.
   * Files are merged into the app (clean=false) or the app is fully replaced (clean=true).
   * POST /api/apps/{id}/upload
   */
  async uploadZip(
    id: string,
    zipFile: File,
    clean = false,
  ): Promise<{
    success: boolean
    written: string[]
    skipped: { path: string; reason: string; ruleSource: string }[]
    errors: { path: string; reason: string }[]
  }> {
    const form = new FormData()
    form.append('file', zipFile)
    const { data } = await apiClient.post(`/api/apps/${id}/upload?clean=${clean}`, form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
    return data
  }

  /**
   * Build file tree from flat list of file paths
   */
  buildFileTree(files: string[], options?: { includeMetadata?: boolean }): FileNode[] {
    const root: FileNode[] = []
    const folderMap = new Map<string, FileNode>()
    const includeMetadata = options?.includeMetadata ?? false

    const normalizedFiles = files
      .filter((f) => includeMetadata || f !== '_metadata.json')
      .map((f) => f.replace(/\\/g, '/'))
      .sort()

    for (const filePath of normalizedFiles) {
      const isDirectoryEntry = filePath.endsWith('/')
      const normalizedPath = isDirectoryEntry ? filePath.slice(0, -1) : filePath
      if (!normalizedPath) {
        continue
      }

      const parts = normalizedPath.split('/')
      let currentLevel = root
      let currentPath = ''

      for (let i = 0; i < parts.length; i++) {
        const part = parts[i]!
        currentPath = currentPath ? `${currentPath}/${part}` : part
        const isTerminal = i === parts.length - 1
        const isFile = isTerminal && !isDirectoryEntry

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

    return this.sortFileNodes(root)
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

  async getScopedFileTree(id: string, scope: AppFileSystemScope): Promise<FileNode[]> {
    const response = await this.getScopedFiles(id, scope)
    if (response.success && response.files) {
      const filePaths = response.files
        .map((f) => (typeof f === 'string' ? f : (f as AppFile).path))
        .filter((p): p is string => Boolean(p))
      return this.buildFileTree(filePaths)
    }
    return []
  }

  async getRootFileTree(id: string): Promise<FileNode[]> {
    const response = await this.getRootFiles(id)
    if (response.success && response.files) {
      const filePaths = response.files
        .map((f) => (typeof f === 'string' ? f : (f as AppFile).path))
        .filter((path): path is string => Boolean(path))

      return this.buildFileTree(filePaths, { includeMetadata: true })
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
  async getPreviewHtml(
    id: string,
    rawHtml?: string,
    testRunId?: string,
    useMockBackend = true,
  ): Promise<string> {
    const app = await this.get(id)
    let html = rawHtml || app.definition?.htmlContent || '<!DOCTYPE html><html lang="en"><head></head><body></body></html>'

    // Get current auth token and base URL for injection
    const token = tokenStorage.getAccessToken() || ''
    const baseUrl = import.meta.env.VITE_API_BASE_URL || ''

    // Strip any existing automatonApi injection to prevent double-injection issues.
    // Old injections used a plain <script> tag (no id), newer ones use id="__automaton_api__".
    html = html.replace(/<script\b[^>]*>[\s\S]*?\(function\s*\(\s*window\s*,\s*APP_(?:ID|SLUG)\b[\s\S]*?<\/script>/gi, '')

    // Inject preview API that makes direct fetch calls
    const resolvedTestRunId = testRunId || `preview-${id}-${Date.now()}`
    const previewScript = buildPreviewApiScript(
      id,
      baseUrl,
      token,
      resolvedTestRunId,
      useMockBackend,
    )

    // Inject base tag for resolving relative paths to app files in preview
    const appBaseUrl = `${baseUrl}/app-preview/${app.userId}/${id}/`
    const baseTag = `<base href="${appBaseUrl}">`

    // Vite production builds often emit root-absolute asset URLs like "/assets/...".
    // In preview those must resolve under /app-preview/{userId}/{appId}/ rather than site root.
    html = html.replace(
      /\b(href|src)=["']\/(?!\/)([^"']+)["']/gi,
      (_, attr: string, assetPath: string) => `${attr}="${assetPath}"`,
    )

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
   * Get version history for an app with pagination
   * GET /api/apps/{appId}/versions
   */
  async getVersions(appId: string, pageNumber: number = 1, pageSize: number = 20, search?: string) {
    const { data } = await apiClient.get(`/api/apps/${appId}/versions`, {
      params: { pageNumber, pageSize, search: search || undefined },
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
  async createVersionSnapshot(appId: string, trigger?: string, summary?: string) {
    const { data } = await apiClient.post(`/api/apps/${appId}/versions/snapshot`, {
      trigger: trigger || 'ManualSave',
      summary: summary?.trim() || undefined,
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

  /**
   * Get all published template apps (public)
   * GET /api/apps/templates
   */
  async getTemplates(): Promise<App[]> {
    const { data } = await apiClient.get<App[]>('/api/apps/templates')
    return data
  }

  /**
   * Duplicate an owned app (copies files + metadata)
   * POST /api/apps/{id}/duplicate
   */
  async duplicate(id: string): Promise<App> {
    const { data } = await apiClient.post<App>(`/api/apps/${id}/duplicate`)
    return data
  }

  /**
   * Get published source metadata for the guided fork flow.
   * GET /api/apps/{id}/fork-source
   */
  async getForkSource(id: string): Promise<ForkAppSource> {
    const { data } = await apiClient.get<ForkAppSource>(`/api/apps/${id}/fork-source`)
    return data
  }

  /**
   * Fork a published marketplace app into the current user's account.
   * POST /api/apps/{id}/fork
   */
  async fork(id: string, request: ForkAppRequest = {}): Promise<App> {
    const { data } = await apiClient.post<App>(`/api/apps/${id}/fork`, request)
    return data
  }

  /**
   * Set template (sample) status for an app — admin only
   * PUT /api/apps/{id}/sample
   */
  async setTemplateStatus(id: string, isSample: boolean, isFeatured: boolean = false): Promise<void> {
    await apiClient.put(`/api/apps/${id}/sample`, { isSample, isFeatured })
  }
}

export const appsService = new AppsService()
