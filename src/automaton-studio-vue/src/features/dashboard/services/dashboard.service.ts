/**
 * Dashboard service for API calls
 * Connects to ASP.NET backend dashboard endpoints
 */
import apiClient from '@/shared/services/api/axios-instance'
import type {
  DashboardStats,
  RecentExecution,
  RecentActivity,
} from '../types/dashboard.types'
import type { App } from '@/features/apps/types/apps.types'

class DashboardService {
  /**
   * Get dashboard statistics
   * GET /api/dashboard/stats
   */
  async getStats(): Promise<DashboardStats> {
    const { data } = await apiClient.get<DashboardStats>('/api/dashboard/stats')
    return data
  }

  /**
   * Get recent flow executions
   * GET /api/flowexecutions/recent/{count}
   */
  async getRecentExecutions(count: number = 5): Promise<RecentExecution[]> {
    const { data } = await apiClient.get<RecentExecution[]>(
      `/api/flowexecutions/recent/${count}`,
    )
    return data
  }

  /**
   * Get recent activities
   * GET /api/recentactivities?count={count}
   */
  async getRecentActivities(count: number = 10): Promise<RecentActivity[]> {
    const { data } = await apiClient.get<RecentActivity[]>('/api/recentactivities', {
      params: { count },
    })
    return data
  }

  /**
   * Get sample apps for onboarding/dashboard
   * GET /api/apps/samples
   */
  async getSampleApps(): Promise<App[]> {
    const { data } = await apiClient.get<App[]>('/api/apps/samples')
    return data
  }
}

export const dashboardService = new DashboardService()
