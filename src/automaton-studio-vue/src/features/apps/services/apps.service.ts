/**
 * Apps service for API calls
 * Connects to ASP.NET backend apps endpoints
 */
import apiClient from '@/shared/services/api/axios-instance'
import type {
  AppSecretSummary,
  AppDeploymentTarget,
  App,
  AppListItem,
  AppTagSettings,
  AppScreenshot,
  PublishAppRequest,
  ExportAppResponse,
  LinkedStepInfo,
  FileNode,
  CreateAppStepRequest,
  ApplicationContract,
} from '../types/apps.types'
import { getFileIcon } from '../types/apps.types'

export type DeployAppResult = {
  success: boolean
  message?: string
  state?: string
  requiresRetry?: boolean
  actions?: Array<{ key: string; status: string; deploymentHash?: string; message?: string }>
}

export type AppActionExecution = {
  id: string
  appId: string
  moduleKey: string
  status: string
  exitCode?: number
  errorCode?: string
  failureSummary?: string
  createdAtUtc: string
  startedAtUtc?: string
  completedAtUtc?: string
}

export type AppActionVersion = {
  version: string
  contentHash?: string
  createdAtUtc: string
  sizeBytes: number
  isCurrent: boolean
}

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

  /**
   * Get tag suggestions for the active personal or project context.
   */
  async getTagSuggestions(projectId?: string): Promise<string[]> {
    const params = projectId ? { projectId } : {}
    const { data } = await apiClient.get<string[]>('/api/apps/tags', { params })
    return data
  }

  async listSecrets(appId: string): Promise<AppSecretSummary[]> {
    const { data } = await apiClient.get<AppSecretSummary[]>(`/api/apps/${appId}/secrets`)
    return data
  }

  async setSecret(appId: string, name: string, value: string): Promise<void> {
    await apiClient.put(`/api/apps/${appId}/secrets/${encodeURIComponent(name)}`, { value })
  }

  async deleteSecret(appId: string, name: string): Promise<void> {
    await apiClient.delete(`/api/apps/${appId}/secrets/${encodeURIComponent(name)}`)
  }

  async getTagSettings(id: string): Promise<AppTagSettings> {
    const { data } = await apiClient.get<AppTagSettings>(`/api/apps/${id}/tag-settings`)
    return data
  }

  async updateTagSettings(id: string, tags: string[]): Promise<void> {
    await apiClient.put(`/api/apps/${id}/tag-settings`, { tags })
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

  async getApps(projectId?: string): Promise<AppListItem[]> {
    const params = projectId ? { projectId } : {}
    const { data } = await apiClient.get<AppListItem[]>('/api/apps', { params })
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

  async listDeploymentTargets(id: string): Promise<AppDeploymentTarget[]> {
    const { data } = await apiClient.get<AppDeploymentTarget[]>(`/api/apps/${id}/targets`)
    return data
  }

  async createDeploymentTarget(
    id: string,
    request: { name: string; slug: string; runnerId?: string | null },
  ): Promise<AppDeploymentTarget> {
    const { data } = await apiClient.post<AppDeploymentTarget>(`/api/apps/${id}/targets`, request)
    return data
  }

  async updateDeploymentTarget(
    id: string,
    targetId: string,
    request: { name: string; slug: string },
  ): Promise<AppDeploymentTarget> {
    const { data } = await apiClient.patch<AppDeploymentTarget>(`/api/apps/${id}/targets/${targetId}`, request)
    return data
  }

  async assignDeploymentTargetRunner(
    id: string,
    targetId: string,
    runnerId: string | null,
    cleanupDeployedRuntime: boolean,
  ): Promise<AppDeploymentTarget> {
    const { data } = await apiClient.post<AppDeploymentTarget>(
      `/api/apps/${id}/targets/${targetId}/assign-runner`,
      { runnerId, cleanupDeployedRuntime },
    )
    return data
  }

  async makeDefaultDeploymentTarget(id: string, targetId: string): Promise<AppDeploymentTarget> {
    const { data } = await apiClient.post<AppDeploymentTarget>(
      `/api/apps/${id}/targets/${targetId}/make-default`,
      {},
    )
    return data
  }

  async deleteDeploymentTarget(id: string, targetId: string): Promise<void> {
    await apiClient.delete(`/api/apps/${id}/targets/${targetId}`)
  }

  async runAction(id: string, actionKey: string, input: unknown): Promise<AppActionExecution> {
    const { data } = await apiClient.post<AppActionExecution>(
      `/api/apps/${id}/actions/${encodeURIComponent(actionKey)}/executions`,
      { input },
    )
    return data
  }

  async getActionArtifact(id: string, executionId: string, kind: 'logs' | 'result') {
    const { data } = await apiClient.get<{ available: boolean; content?: string; message?: string }>(
      `/api/apps/${id}/action-executions/${executionId}/${kind}`,
    )
    return data
  }

  async cancelAction(id: string, executionId: string): Promise<boolean> {
    const { data } = await apiClient.post<{ cancelled: boolean }>(
      `/api/apps/${id}/action-executions/${executionId}/cancel`,
      {},
    )
    return data.cancelled
  }

  async getActionExecution(id: string, executionId: string): Promise<AppActionExecution> {
    const { data } = await apiClient.get<AppActionExecution>(`/api/apps/${id}/action-executions/${executionId}`)
    return data
  }

  async listActionExecutions(id: string): Promise<AppActionExecution[]> {
    const { data } = await apiClient.get<AppActionExecution[]>(`/api/apps/${id}/action-executions`)
    return data
  }

  async listActionVersions(id: string, actionKey: string): Promise<AppActionVersion[]> {
    const { data } = await apiClient.get<{ versions: AppActionVersion[] }>(`/api/apps/${id}/actions/${encodeURIComponent(actionKey)}/versions`)
    return data.versions
  }

  async rollbackAction(id: string, actionKey: string, version: string): Promise<void> {
    await apiClient.post(`/api/apps/${id}/actions/${encodeURIComponent(actionKey)}/rollback`, { version })
  }

  async deleteActionVersion(id: string, actionKey: string, version: string): Promise<void> {
    await apiClient.delete(`/api/apps/${id}/actions/${encodeURIComponent(actionKey)}/versions/${encodeURIComponent(version)}`)
  }

  async createRuntimeLaunch(id: string): Promise<{ runtimeToken: string }> {
    const { data } = await apiClient.post<{ runtimeToken: string }>(`/api/apps/${id}/runtime-launch`, {})
    return data
  }

  getRestrictedRuntimeLaunchUrl(id: string): string {
    const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL || window.location.origin).replace(/\/+$/, '')
    return `${apiBaseUrl}/api/apps/${encodeURIComponent(id)}/launch-runtime`
  }

  /**
   * Update the execution runner for an app
   * PUT /api/apps/{id}/runner
   */
  async updateRunner(id: string, runnerId: string | null): Promise<void> {
    await apiClient.put(`/api/apps/${id}/runner`, { executionRunnerId: runnerId })
  }

  async getMarketplaceCategories(): Promise<string[]> {
    const response = await apiClient.get<string[]>('/api/public/marketplace/categories')
    return response.data ?? []
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
      category?: string | null
      application?: ApplicationContract
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
      runtimeVisibility: app.runtimeVisibility,
      catalogVisibility: app.catalogVisibility,
      marketplacePreviewMode: app.marketplacePreviewMode,
      created: app.created,
      updated: app.updated,
      executionRunnerId: settings.executionRunnerId ?? app.executionRunnerId,
      isProfileFeatured: settings.isProfileFeatured ?? app.isProfileFeatured,
      definition: app.definition,
      thumbnailUrl: app.thumbnailUrl,
      screenshots: app.screenshots,
      category: settings.category !== undefined ? settings.category : app.category,
      tags: app.tags,
      application: settings.application ?? app.application,
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
      runtimeVisibility: app.runtimeVisibility,
      catalogVisibility: app.catalogVisibility,
      marketplacePreviewMode: app.marketplacePreviewMode,
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
   * Generate slug from app name
   */
  generateSlug(name: string): string {
    return name
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-|-$/g, '')
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
