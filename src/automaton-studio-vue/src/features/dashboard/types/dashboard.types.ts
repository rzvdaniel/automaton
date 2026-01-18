/**
 * Dashboard types
 * TypeScript interfaces for dashboard data structures
 */

/**
 * Daily execution trend data point
 */
export interface DailyExecutionTrend {
  date: string // ISO date string
  successCount: number
  failureCount: number
  totalCount: number
}

/**
 * Flow health metric
 */
export interface FlowHealthMetric {
  flowId: string
  flowName: string
  executionCount: number
  successRate: number
  lastExecuted: string // ISO date string
}

/**
 * Recent app metric for dashboard
 */
export interface RecentAppMetric {
  id: string
  name: string
  displayName: string
  isPublished: boolean
  publicUrl?: string
  viewCount: number
  updated: string // ISO date string
}

/**
 * Published app metric for dashboard
 */
export interface PublishedAppMetric {
  id: string
  name: string
  displayName: string
  publicUrl: string
  viewCount: number
  publishedAt?: string // ISO date string
}

/**
 * Dashboard statistics from API
 */
export interface DashboardStats {
  // Legacy fields (flows-focused)
  totalFlows: number
  totalRunners: number
  onlineRunners: number
  successRate: number
  recentExecutions: number
  totalApps: number
  totalCustomSteps: number
  activeSchedules: number
  executionsToday: number
  executionsThisWeek: number
  executionsThisMonth: number
  executionTrends: DailyExecutionTrend[]
  topFlows: FlowHealthMetric[]
  // Apps-focused metrics
  publishedApps: number
  totalAppViews: number
  recentApps: RecentAppMetric[]
  publishedAppsList: PublishedAppMetric[]
}

/**
 * Recent flow execution
 */
export interface RecentExecution {
  id: string
  flowId: string
  started: string // ISO date string
  finished: string // ISO date string
  status: string // 'Success' | 'Error' | 'Running' | 'Pending'
  application: string
}

/**
 * Activity type enum
 */
export type ActivityType =
  | 'None'
  | 'FlowCreated'
  | 'FlowUpdated'
  | 'FlowDeleted'
  | 'FlowExecuted'
  | 'CustomStepCreated'
  | 'CustomStepUpdated'
  | 'CustomStepDeleted'
  | 'RunnerAdded'

/**
 * Recent activity
 */
export interface RecentActivity {
  id: string
  activityType: ActivityType
  entityName: string
  entityId?: string
  description: string
  createdAt: string // ISO date string
  userId: string
  userName: string
}

/**
 * Dashboard data combining stats and recent executions
 */
export interface DashboardData {
  stats: DashboardStats | null
  recentExecutions: RecentExecution[]
  recentActivities: RecentActivity[]
}
