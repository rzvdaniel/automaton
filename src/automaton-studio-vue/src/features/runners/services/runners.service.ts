/**
 * Runners service for API calls
 * Connects to ASP.NET backend runners endpoints
 */
import apiClient from '@/shared/services/api/axios-instance'
import type {
  CreateSelfHostedRunnerInstallationRequest,
  Runner,
  RunnerRemovalInstructions,
  RunnerUpgradeInstructions,
  RunnerStatusResponse,
  SelfHostedRunnerInstallation,
  SelfHostedRunnerInstallationStatusResponse,
} from '../types/runners.types'

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
    await apiClient.delete(`/api/runners/${id}`, { params: { hostCleanupConfirmed: true } })
  }

  async prepareUpgrade(id: string): Promise<RunnerUpgradeInstructions> {
    const { data } = await apiClient.post<RunnerUpgradeInstructions>(`/api/runners/${id}/upgrade`)
    return data
  }

  async prepareRemoval(id: string): Promise<RunnerRemovalInstructions> {
    const { data } = await apiClient.post<RunnerRemovalInstructions>(`/api/runners/${id}/removal`)
    return data
  }


  async createSelfHostedInstallation(
    request: CreateSelfHostedRunnerInstallationRequest
  ): Promise<SelfHostedRunnerInstallation> {
    const { data } = await apiClient.post<SelfHostedRunnerInstallation>(
      '/api/self-hosted-runners/installations',
      request
    )
    return data
  }

  async getSelfHostedInstallationStatus(
    installationId: string
  ): Promise<SelfHostedRunnerInstallationStatusResponse> {
    const { data } = await apiClient.get<SelfHostedRunnerInstallationStatusResponse>(
      `/api/self-hosted-runners/installations/${encodeURIComponent(installationId)}/status`
    )
    return data
  }
}

export const runnersService = new RunnersService()
