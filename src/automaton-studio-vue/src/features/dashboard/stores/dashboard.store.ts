/**
 * Dashboard Pinia store
 * Manages dashboard data, loading states, and auto-refresh
 */
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { dashboardService } from '../services/dashboard.service'
import type {
  DashboardStats,
  RecentExecution,
  RecentActivity,
} from '../types/dashboard.types'

export const useDashboardStore = defineStore('dashboard', () => {
  // State
  const stats = ref<DashboardStats | null>(null)
  const recentExecutions = ref<RecentExecution[]>([])
  const recentActivities = ref<RecentActivity[]>([])
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const lastRefreshed = ref<Date | null>(null)

  // Auto-refresh interval ID
  let refreshIntervalId: ReturnType<typeof setInterval> | null = null

  // Computed
  const hasData = computed(() => stats.value !== null)

  const activeFlowsCount = computed(() => stats.value?.totalFlows ?? 0)

  const executionsLast24h = computed(() => stats.value?.executionsToday ?? 0)

  const successRate = computed(() => stats.value?.successRate ?? 0)

  const runnersOnline = computed(() => stats.value?.onlineRunners ?? 0)

  const runnersTotal = computed(() => stats.value?.totalRunners ?? 0)

  const executionTrends = computed(() => stats.value?.executionTrends ?? [])

  const topFlows = computed(() => stats.value?.topFlows ?? [])

  // Actions
  async function fetchDashboard(): Promise<void> {
    isLoading.value = true
    error.value = null

    try {
      // Fetch all dashboard data in parallel
      const [statsData, executionsData, activitiesData] = await Promise.all([
        dashboardService.getStats(),
        dashboardService.getRecentExecutions(5),
        dashboardService.getRecentActivities(10),
      ])

      stats.value = statsData
      recentExecutions.value = executionsData
      recentActivities.value = activitiesData
      lastRefreshed.value = new Date()
    } catch (err: unknown) {
      const errorMessage = getErrorMessage(err)
      error.value = errorMessage
      throw new Error(errorMessage)
    } finally {
      isLoading.value = false
    }
  }

  async function refreshStats(): Promise<void> {
    // Silent refresh - don't show loading state
    try {
      const [statsData, executionsData] = await Promise.all([
        dashboardService.getStats(),
        dashboardService.getRecentExecutions(5),
      ])

      stats.value = statsData
      recentExecutions.value = executionsData
      lastRefreshed.value = new Date()
      error.value = null
    } catch (err: unknown) {
      // Silent fail for auto-refresh
      console.error('Failed to refresh dashboard:', err)
    }
  }

  function startAutoRefresh(intervalMs: number = 30000): void {
    stopAutoRefresh()
    refreshIntervalId = setInterval(() => {
      refreshStats()
    }, intervalMs)
  }

  function stopAutoRefresh(): void {
    if (refreshIntervalId) {
      clearInterval(refreshIntervalId)
      refreshIntervalId = null
    }
  }

  function clearError(): void {
    error.value = null
  }

  function reset(): void {
    stats.value = null
    recentExecutions.value = []
    recentActivities.value = []
    isLoading.value = false
    error.value = null
    lastRefreshed.value = null
    stopAutoRefresh()
  }

  // Helper to extract error message
  function getErrorMessage(err: unknown): string {
    if (err && typeof err === 'object') {
      // Check for Axios error response
      const axiosError = err as {
        response?: { data?: { message?: string; title?: string }; status?: number }
        message?: string
      }
      if (axiosError.response?.data?.message) {
        return axiosError.response.data.message
      }
      if (axiosError.response?.data?.title) {
        return axiosError.response.data.title
      }
      if (axiosError.response?.status === 401) {
        return 'Authentication required. Please log in again.'
      }
      if (axiosError.response?.status === 403) {
        return 'You do not have permission to view the dashboard.'
      }
      if (axiosError.response?.status === 500) {
        return 'Server error. Please try again later.'
      }
      // Check for standard Error
      if (axiosError.message) {
        return axiosError.message
      }
    }
    return 'Failed to load dashboard data'
  }

  return {
    // State
    stats,
    recentExecutions,
    recentActivities,
    isLoading,
    error,
    lastRefreshed,
    // Computed
    hasData,
    activeFlowsCount,
    executionsLast24h,
    successRate,
    runnersOnline,
    runnersTotal,
    executionTrends,
    topFlows,
    // Actions
    fetchDashboard,
    refreshStats,
    startAutoRefresh,
    stopAutoRefresh,
    clearError,
    reset,
  }
})
