import apiClient from '@/shared/services/api/axios-instance'
import type { AiGuidanceAdmin, UpsertAiGuidanceRequest } from '../types/admin.types'

class AdminAiGuidanceService {
  async getAll(): Promise<AiGuidanceAdmin[]> {
    const { data } = await apiClient.get<AiGuidanceAdmin[]>('/api/admin/ai-guidance')
    return data
  }

  async getById(id: string): Promise<AiGuidanceAdmin> {
    const { data } = await apiClient.get<AiGuidanceAdmin>(`/api/admin/ai-guidance/${id}`)
    return data
  }

  async create(request: UpsertAiGuidanceRequest): Promise<AiGuidanceAdmin> {
    const { data } = await apiClient.post<AiGuidanceAdmin>('/api/admin/ai-guidance', request)
    return data
  }

  async update(id: string, request: UpsertAiGuidanceRequest): Promise<AiGuidanceAdmin> {
    const { data } = await apiClient.put<AiGuidanceAdmin>(`/api/admin/ai-guidance/${id}`, request)
    return data
  }

  async enable(id: string): Promise<AiGuidanceAdmin> {
    const { data } = await apiClient.post<AiGuidanceAdmin>(`/api/admin/ai-guidance/${id}/enable`)
    return data
  }

  async disable(id: string): Promise<AiGuidanceAdmin> {
    const { data } = await apiClient.post<AiGuidanceAdmin>(`/api/admin/ai-guidance/${id}/disable`)
    return data
  }

  async reset(id: string): Promise<AiGuidanceAdmin> {
    const { data } = await apiClient.post<AiGuidanceAdmin>(`/api/admin/ai-guidance/${id}/reset`)
    return data
  }

  async delete(id: string): Promise<void> {
    await apiClient.delete(`/api/admin/ai-guidance/${id}`)
  }
}

export const adminAiGuidanceService = new AdminAiGuidanceService()
