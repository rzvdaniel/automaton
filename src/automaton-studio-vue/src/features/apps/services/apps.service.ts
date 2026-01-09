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
} from '../types/apps.types'

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
}

export const appsService = new AppsService()
