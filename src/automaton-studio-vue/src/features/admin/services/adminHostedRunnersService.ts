import apiClient from '@/shared/services/api/axios-instance'
import type {
  HostedRunner,
  ProvisionHostedRunnerRequest,
  RunnerHost,
  RunnerEnrollment,
  InfrastructureRunnerEnrollmentKey,
  InfrastructureWorkflowExecution,
  InfrastructureWorkflowExecutionLog,
  UpdateRunnerHostRequest,
  HostedRunnerSpecification,
  HostedRunnerSpecificationRequest,
  HostedRunnerPlatformSettings,
} from '../types/admin.types'

class AdminHostedRunnersService {
  async getHosts(): Promise<RunnerHost[]> {
    const response = await apiClient.get<RunnerHost[]>('/api/admin/hosted-runners/hosts')
    return response.data
  }

  async createInfrastructureRunnerEnrollmentKey(): Promise<InfrastructureRunnerEnrollmentKey> {
    const response = await apiClient.post<InfrastructureRunnerEnrollmentKey>(
      '/api/admin/hosted-runners/infrastructure-runner/enrollment-keys',
    )
    return response.data
  }

  async getInfrastructureRunnerEnrollmentKeys(): Promise<InfrastructureRunnerEnrollmentKey[]> {
    const response = await apiClient.get<InfrastructureRunnerEnrollmentKey[]>(
      '/api/admin/hosted-runners/infrastructure-runner/enrollment-keys',
    )
    return response.data
  }

  async revokeInfrastructureRunnerEnrollmentKey(id: string): Promise<void> {
    await apiClient.delete(`/api/admin/hosted-runners/infrastructure-runner/enrollment-keys/${id}`)
  }

  async updateHost(id: string, request: UpdateRunnerHostRequest): Promise<RunnerHost> {
    const response = await apiClient.put<RunnerHost>(`/api/admin/hosted-runners/hosts/${id}`, request)
    return response.data
  }

  async approveHost(id: string): Promise<RunnerHost> {
    const response = await apiClient.post<RunnerHost>(`/api/admin/hosted-runners/hosts/${id}/approve`)
    return response.data
  }

  async disableHostScheduling(id: string): Promise<RunnerHost> {
    const response = await apiClient.post<RunnerHost>(`/api/admin/hosted-runners/hosts/${id}/disable-scheduling`)
    return response.data
  }

  async revokeInfrastructureRunner(id: string): Promise<RunnerHost> {
    const response = await apiClient.post<RunnerHost>(`/api/admin/hosted-runners/hosts/${id}/revoke-infrastructure-runner`)
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

  async startHostValidation(hostId: string, runnerId?: string): Promise<InfrastructureWorkflowExecution> {
    const response = await apiClient.post<InfrastructureWorkflowExecution>('/api/admin/infrastructure-workflows/host-validation', {
      runnerHostId: hostId,
      runnerId,
      idempotencyKey: `studio-host-validation-${hostId}-${crypto.randomUUID()}`,
    })
    return response.data
  }

  async getInfrastructureWorkflowLogs(executionId: string): Promise<InfrastructureWorkflowExecutionLog[]> {
    const response = await apiClient.get<InfrastructureWorkflowExecutionLog[]>(
      `/api/admin/infrastructure-workflows/executions/${executionId}/logs`,
    )
    return response.data
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

  async getSpecifications(): Promise<HostedRunnerSpecification[]> {
    const response = await apiClient.get<HostedRunnerSpecification[]>('/api/admin/hosted-runner-specifications')
    return response.data
  }

  async getSpecification(id: string): Promise<HostedRunnerSpecification> {
    const response = await apiClient.get<HostedRunnerSpecification>(`/api/admin/hosted-runner-specifications/${id}`)
    return response.data
  }

  async createSpecification(request: HostedRunnerSpecificationRequest): Promise<HostedRunnerSpecification> {
    const response = await apiClient.post<HostedRunnerSpecification>('/api/admin/hosted-runner-specifications', request)
    return response.data
  }

  async updateSpecification(id: string, request: HostedRunnerSpecificationRequest): Promise<HostedRunnerSpecification> {
    const response = await apiClient.put<HostedRunnerSpecification>(`/api/admin/hosted-runner-specifications/${id}`, request)
    return response.data
  }

  async duplicateSpecification(id: string): Promise<HostedRunnerSpecification> {
    const response = await apiClient.post<HostedRunnerSpecification>(`/api/admin/hosted-runner-specifications/${id}/duplicate`)
    return response.data
  }

  async setSpecificationActive(id: string, active: boolean): Promise<HostedRunnerSpecification> {
    const response = await apiClient.post<HostedRunnerSpecification>(`/api/admin/hosted-runner-specifications/${id}/${active ? 'activate' : 'deactivate'}`)
    return response.data
  }

  async getSpecificationSettings(): Promise<HostedRunnerPlatformSettings> {
    const response = await apiClient.get<HostedRunnerPlatformSettings>('/api/admin/hosted-runner-settings')
    return response.data
  }

  async updateSpecificationSettings(request: Pick<HostedRunnerPlatformSettings, 'provisioningEnabled'>): Promise<HostedRunnerPlatformSettings> {
    const response = await apiClient.put<HostedRunnerPlatformSettings>('/api/admin/hosted-runner-settings', request)
    return response.data
  }
}

export const adminHostedRunnersService = new AdminHostedRunnersService()
