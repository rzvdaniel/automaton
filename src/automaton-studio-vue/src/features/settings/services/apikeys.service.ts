/**
 * API Keys service for API calls
 * Connects to ASP.NET backend API keys endpoints
 */
import apiClient from '@/shared/services/api/axios-instance'
import type {
  ApiKey,
  CreateApiKeyRequest,
  CreateApiKeyResponse,
} from '../types/apikeys.types'

class ApiKeysService {
  /**
   * Get all API keys for the current user
   * GET /api/apikeys
   */
  async list(projectId?: string): Promise<ApiKey[]> {
    const params = projectId ? { projectId } : {}
    const { data } = await apiClient.get<ApiKey[]>('/api/apikeys', { params })
    return data
  }

  /**
   * Create a new API key
   * POST /api/apikeys
   */
  async create(request: CreateApiKeyRequest): Promise<CreateApiKeyResponse> {
    const { data } = await apiClient.post<CreateApiKeyResponse>('/api/apikeys', request)
    return data
  }

  /**
   * Permanently delete an API key
   * DELETE /api/apikeys/{id}
   */
  async delete(id: string): Promise<void> {
    await apiClient.delete(`/api/apikeys/${id}`)
  }
}

export const apiKeysService = new ApiKeysService()
