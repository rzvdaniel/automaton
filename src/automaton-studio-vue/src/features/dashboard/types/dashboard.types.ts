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
 * Dashboard statistics from API
 */
export interface DashboardStats {
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
