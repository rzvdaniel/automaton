/**
 * App Execution Log Service
 * Fetches runtime execution logs for published app controllers
 */
import axiosInstance from './axios-instance'
import type { AppExecutionLogPage, AppExecutionLogFilter } from '@/shared/types/ai.types'

export const appExecutionLogService = {
  /**
   * Get execution logs for an app with optional filters
   * GET /api/apps/{appId}/execution-logs
   */
  async getLogs(appId: string, filter?: AppExecutionLogFilter): Promise<AppExecutionLogPage> {
    const params: Record<string, any> = {}
    if (filter?.success !== undefined) params.success = filter.success
    if (filter?.controllerName) params.controllerName = filter.controllerName
    if (filter?.from) params.from = filter.from
    if (filter?.to) params.to = filter.to
    if (filter?.page) params.page = filter.page
    if (filter?.pageSize) params.pageSize = filter.pageSize

    const response = await axiosInstance.get<AppExecutionLogPage>(
      `/api/apps/${appId}/execution-logs`,
      { params },
    )
    return response.data
  },
}
