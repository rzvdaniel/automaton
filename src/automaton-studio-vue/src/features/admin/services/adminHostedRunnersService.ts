import apiClient from '@/shared/services/api/axios-instance'
import type {
  CreateRunnerHostRequest,
  HostedRunner,
  ProvisionHostedRunnerRequest,
  RunnerHost,
  RunnerEnrollment,
  UpdateRunnerHostRequest,
} from '../types/admin.types'

class AdminHostedRunnersService {
  async getHosts(): Promise<RunnerHost[]> {
    const response = await apiClient.get<RunnerHost[]>('/api/admin/hosted-runners/hosts')
    return response.data
  }

  async createHost(request: CreateRunnerHostRequest): Promise<RunnerHost> {
    const response = await apiClient.post<RunnerHost>('/api/admin/hosted-runners/hosts', request)
    return response.data
  }

  async updateHost(id: string, request: UpdateRunnerHostRequest): Promise<RunnerHost> {
    const response = await apiClient.put<RunnerHost>(`/api/admin/hosted-runners/hosts/${id}`, request)
    return response.data
  }

  async createInfrastructureRunnerEnrollment(hostId: string): Promise<RunnerEnrollment> {
    const response = await apiClient.post<RunnerEnrollment>(
      `/api/admin/hosted-runners/hosts/${hostId}/infrastructure-runner/enrollments`,
    )
    return response.data
  }

  async revokeInfrastructureRunnerEnrollment(hostId: string, enrollmentId: string): Promise<void> {
    await apiClient.delete(`/api/admin/hosted-runners/hosts/${hostId}/infrastructure-runner/enrollments/${enrollmentId}`)
  }

  async getHostedRunners(): Promise<HostedRunner[]> {
    const response = await apiClient.get<HostedRunner[]>('/api/admin/hosted-runners/runners')
    return response.data
  }

  async provisionForUser(userId: string, request: ProvisionHostedRunnerRequest = {}): Promise<HostedRunner> {
    const response = await apiClient.post<HostedRunner>(`/api/admin/hosted-runners/users/${userId}/provision`, request)
    return response.data
  }

  async restartHostedRunner(id: string): Promise<HostedRunner> {
    const response = await apiClient.post<HostedRunner>(`/api/admin/hosted-runners/runners/${id}/restart`)
    return response.data
  }

  async reprovisionHostedRunner(id: string): Promise<HostedRunner> {
    const response = await apiClient.post<HostedRunner>(`/api/admin/hosted-runners/runners/${id}/reprovision`)
    return response.data
  }

  async deleteHostedRunner(id: string): Promise<void> {
    await apiClient.delete(`/api/admin/hosted-runners/runners/${id}`)
  }
}

export const adminHostedRunnersService = new AdminHostedRunnersService()
