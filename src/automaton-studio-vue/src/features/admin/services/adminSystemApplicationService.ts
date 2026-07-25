import apiClient from '@/shared/services/api/axios-instance'
import type { ActionExecution, ActionSchedule } from '@/features/apps/services/action-schedules.service'

export interface SystemAction {
  key: string
  name?: string
  description?: string
  trustLevel: string
  schedulable: boolean
  allowedExecutionScopes: string[]
  inputSchema?: Record<string, unknown>
  timeoutSeconds: number
  minimumIntervalSeconds: number
  deployedHash?: string
  scheduleCount: number
  lastExecution?: ActionExecution
}

export interface SystemApplicationResponse {
  application: { id: string; name: string; readOnly: boolean }
  actions: SystemAction[]
  schedules: ActionSchedule[]
  executions: ActionExecution[]
}

export const adminSystemApplicationService = {
  async get(): Promise<SystemApplicationResponse> {
    return (await apiClient.get('/api/admin/system-application')).data
  },
}
