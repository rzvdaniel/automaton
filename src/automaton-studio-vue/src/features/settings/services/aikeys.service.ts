/**
 * AI API Keys service for API calls
 * Connects to ASP.NET backend AI API keys endpoints
 */
import apiClient from '@/shared/services/api/axios-instance'
import type {
  AiApiKey,
  AiProviderInfo,
  CreateAiApiKeyRequest,
} from '../types/aikeys.types'

class AiApiKeysService {
  /**
   * Get all AI API keys for the current user
   * GET /api/aiApiKeys
   */
  async list(projectId?: string): Promise<AiApiKey[]> {
    const params = projectId ? { projectId } : {}
    const { data } = await apiClient.get<AiApiKey[]>('/api/aiApiKeys', { params })
    return data
  }

  /**
   * Get available AI providers
   * GET /api/aiApiKeys/providers
   */
  async getProviders(includeEnvironment: boolean = false): Promise<AiProviderInfo[]> {
    const { data } = await apiClient.get<AiProviderInfo[]>('/api/aiApiKeys/providers', {
      params: { includeEnvironment },
    })
    return data
  }

  /**
   * Create a new AI API key
   * POST /api/aiApiKeys
   */
  async create(request: CreateAiApiKeyRequest): Promise<AiApiKey> {
    const { data } = await apiClient.post<AiApiKey>('/api/aiApiKeys', request)
    return data
  }

  /**
   * Delete an AI API key
   * DELETE /api/aiApiKeys/{id}
   */
  async delete(id: string): Promise<void> {
    await apiClient.delete(`/api/aiApiKeys/${id}`)
  }
}

export const aiApiKeysService = new AiApiKeysService()
