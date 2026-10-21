import apiClient from '@/shared/services/api/axios-instance'
import type {
  HostedRunner,
  RunnerHost,
  RunnerEnrollment,
  InfrastructureRunnerEnrollmentKey,
  InfrastructureWorkflowExecution,
  InfrastructureWorkflowExecutionLog,
  UpdateRunnerHostRequest,
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

  async getInfrastructureWorkflowExecution(executionId: string): Promise<InfrastructureWorkflowExecution> {
    const response = await apiClient.get<InfrastructureWorkflowExecution>(
      `/api/admin/infrastructure-workflows/executions/${executionId}`,
    )
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

  async restartHostedRunner(id: string): Promise<HostedRunner> {
    const response = await apiClient.post<HostedRunner>(`/api/admin/hosted-runners/runners/${id}/restart`)
    return response.data
  }

  async verifyHostedRunnerStorage(id: string): Promise<void> {
    await apiClient.post(`/api/admin/hosted-runners/runners/${id}/verify-storage`)
  }

  async suspendHostedRunner(id: string): Promise<void> {
    await apiClient.post(`/api/admin/hosted-runners/runners/${id}/suspend`)
  }

  async resumeHostedRunner(id: string): Promise<void> {
    await apiClient.post(`/api/admin/hosted-runners/runners/${id}/resume`)
  }

  async extendStorageGracePeriod(id: string, hours: number): Promise<void> {
    await apiClient.post(`/api/admin/hosted-runners/runners/${id}/extend-grace`, { hours })
  }

  async clearStorageEnforcement(id: string): Promise<void> {
    await apiClient.post(`/api/admin/hosted-runners/runners/${id}/clear-enforcement`)
  }

  async deleteHostedRunner(id: string, dataDisposition: 'preserve' | 'delete'): Promise<void> {
    await apiClient.delete(`/api/admin/hosted-runners/runners/${id}`, {
      data: { dataDisposition },
    })
  }

}

export const adminHostedRunnersService = new AdminHostedRunnersService()
