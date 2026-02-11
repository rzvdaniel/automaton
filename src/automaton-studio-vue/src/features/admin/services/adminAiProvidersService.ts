/**
 * Admin AI Providers Service
 * Handles API calls for managing AI providers and models
 */
import apiClient from '@/shared/services/api/axios-instance'
import type {
  AiProviderAdmin,
  AiModelAdmin,
  CreateAiProviderRequest,
  UpdateAiProviderRequest,
  CreateAiModelRequest,
  UpdateAiModelRequest,
} from '../types/admin.types'

class AdminAiProvidersService {
  /**
   * Get all AI providers
   * GET /api/admin/ai-providers
   */
  async getProviders(): Promise<AiProviderAdmin[]> {
    const { data } = await apiClient.get<AiProviderAdmin[]>('/api/admin/ai-providers')
    return data
  }

  /**
   * Get a single AI provider by ID
   * GET /api/admin/ai-providers/{id}
   */
  async getProvider(id: string): Promise<AiProviderAdmin> {
    const { data } = await apiClient.get<AiProviderAdmin>(`/api/admin/ai-providers/${id}`)
    return data
  }

  /**
   * Create a new AI provider
   * POST /api/admin/ai-providers
   */
  async createProvider(request: CreateAiProviderRequest): Promise<AiProviderAdmin> {
    const { data } = await apiClient.post<AiProviderAdmin>('/api/admin/ai-providers', request)
    return data
  }

  /**
   * Update an AI provider
   * PUT /api/admin/ai-providers/{id}
   */
  async updateProvider(id: string, request: UpdateAiProviderRequest): Promise<void> {
    await apiClient.put(`/api/admin/ai-providers/${id}`, request)
  }

  /**
   * Delete an AI provider
   * DELETE /api/admin/ai-providers/{id}
   */
  async deleteProvider(id: string): Promise<void> {
    await apiClient.delete(`/api/admin/ai-providers/${id}`)
  }

  /**
   * Get all models for a provider
   * GET /api/admin/ai-providers/{providerId}/models
   */
  async getModelsByProvider(providerId: string): Promise<AiModelAdmin[]> {
    const { data } = await apiClient.get<AiModelAdmin[]>(
      `/api/admin/ai-providers/${providerId}/models`
    )
    return data
  }

  /**
   * Get all models across all providers
   * GET /api/admin/ai-models
   */
  async getAllModels(): Promise<AiModelAdmin[]> {
    const { data } = await apiClient.get<AiModelAdmin[]>('/api/admin/ai-models')
    return data
  }

  /**
   * Create a new model for a provider
   * POST /api/admin/ai-providers/{providerId}/models
   */
  async createModel(providerId: string, request: CreateAiModelRequest): Promise<AiModelAdmin> {
    const { data } = await apiClient.post<AiModelAdmin>(
      `/api/admin/ai-providers/${providerId}/models`,
      request
    )
    return data
  }

  /**
   * Update an existing model
   * PUT /api/admin/ai-models/{id}
   */
  async updateModel(
    modelId: string,
    request: UpdateAiModelRequest
  ): Promise<void> {
    await apiClient.put(`/api/admin/ai-models/${modelId}`, request)
  }

  /**
   * Delete an existing model
   * DELETE /api/admin/ai-models/{id}
   */
  async deleteModel(modelId: string): Promise<void> {
    await apiClient.delete(`/api/admin/ai-models/${modelId}`)
  }
}

export const adminAiProvidersService = new AdminAiProvidersService()
