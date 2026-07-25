import apiClient from '@/shared/services/api/axios-instance'

export interface ActionModule {
  key: string
  name?: string
  description?: string
  timeoutSeconds: number
  contentHash?: string
  schedulable: boolean
  allowedExecutionScopes: string[]
  minimumIntervalSeconds: number
  inputSchema?: Record<string, unknown>
}

export interface ActionSchedule {
  id: string
  applicationId: string
  actionKey: string
  name: string
  description?: string
  executionScope: string
  scheduleType: 'OneTime' | 'Interval' | 'Cron'
  runAtUtc?: string
  intervalSeconds?: number
  cronExpression?: string
  timeZone: string
  enabled: boolean
  nextRunAtUtc?: string
  lastTriggeredAtUtc?: string
  lastExecutionId?: string
  concurrencyPolicy: 'Allow' | 'SkipIfRunning' | 'Queue'
  misfirePolicy: 'Skip' | 'RunOnceNow'
  inputJson?: string
}

export interface ActionExecution {
  id: string
  moduleKey: string
  status: string
  failureSummary?: string
  createdAtUtc: string
  startedAtUtc?: string
  completedAtUtc?: string
  scheduleOccurrenceId?: string
}

export interface SaveSchedule {
  applicationId: string
  actionKey: string
  name: string
  description?: string
  executionScope: string
  scheduleType: string
  runAtUtc?: string
  intervalSeconds?: number
  cronExpression?: string
  timeZone: string
  enabled: boolean
  concurrencyPolicy: string
  misfirePolicy: string
  input: Record<string, unknown>
}

export interface SchedulingCapability {
  enabled: boolean
  maximumSchedules: number
  minimumIntervalSeconds: number
  maximumConcurrentExecutions: number
  monthlyExecutionLimit: number
}

export const actionSchedulesService = {
  async listSchedules(): Promise<ActionSchedule[]> {
    return (await apiClient.get('/api/action-schedules')).data
  },
  async capability(): Promise<SchedulingCapability> {
    return (await apiClient.get('/api/action-schedules/capability')).data
  },
  async create(value: SaveSchedule): Promise<ActionSchedule> {
    return (await apiClient.post('/api/action-schedules', value)).data
  },
  async update(id: string, value: SaveSchedule): Promise<ActionSchedule> {
    return (await apiClient.put(`/api/action-schedules/${id}`, value)).data
  },
  async setEnabled(id: string, enabled: boolean): Promise<ActionSchedule> {
    return (await apiClient.post(`/api/action-schedules/${id}/${enabled ? 'enable' : 'disable'}`)).data
  },
  async remove(id: string): Promise<void> {
    await apiClient.delete(`/api/action-schedules/${id}`)
  },
  async listActions(appId: string): Promise<ActionModule[]> {
    return (await apiClient.get(`/api/apps/${appId}/actions`)).data.actions
  },
  async listExecutions(appId: string): Promise<ActionExecution[]> {
    return (await apiClient.get(`/api/apps/${appId}/action-executions`)).data
  },
  async getLogs(appId: string, executionId: string): Promise<{ available: boolean; content?: string; message?: string }> {
    return (await apiClient.get(`/api/apps/${appId}/action-executions/${executionId}/logs`)).data
  },
}
