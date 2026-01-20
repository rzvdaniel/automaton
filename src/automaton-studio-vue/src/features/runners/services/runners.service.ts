/**
 * Runners service for API calls
 * Connects to ASP.NET backend runners endpoints
 */
import apiClient from '@/shared/services/api/axios-instance'
import type { Runner, RunnerStatusResponse } from '../types/runners.types'

class RunnersService {
  /**
   * Get all runners
   * GET /api/runners
   */
  async list(projectId?: string): Promise<Runner[]> {
    const params = projectId ? { projectId } : {}
    const { data } = await apiClient.get<Runner[]>('/api/runners', { params })
    return data
  }

  /**
   * Get runner status summary
   * GET /api/runners/status
   */
  async getStatus(): Promise<RunnerStatusResponse> {
    const { data } = await apiClient.get<RunnerStatusResponse>('/api/runners/status')
    return data
  }

  /**
   * Delete a runner
   * DELETE /api/runners/{id}
   */
  async delete(id: string): Promise<void> {
    await apiClient.delete(`/api/runners/${id}`)
  }
}

export const runnersService = new RunnersService()
