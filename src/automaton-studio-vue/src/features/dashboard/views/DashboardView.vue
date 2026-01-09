<script setup lang="ts">
import { onMounted, onUnmounted, computed } from 'vue'
import { useRouter } from 'vue-router'
import Card from 'primevue/card'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import Skeleton from 'primevue/skeleton'
import Message from 'primevue/message'
import { useDashboardStore } from '../stores/dashboard.store'
import ExecutionTrendChart from '../components/ExecutionTrendChart.vue'

const router = useRouter()
const dashboardStore = useDashboardStore()

// Fetch dashboard data on mount
onMounted(async () => {
  await dashboardStore.fetchDashboard()
  // Start auto-refresh every 30 seconds
  dashboardStore.startAutoRefresh(30000)
})

// Stop auto-refresh on unmount
onUnmounted(() => {
  dashboardStore.stopAutoRefresh()
})

// Navigation handlers
const handleCreateFlow = () => {
  router.push('/flows/generate')
}

const handleCreateApp = () => {
  router.push('/apps')
}

const handleNewStep = () => {
  router.push('/steps')
}

const handleViewAllExecutions = () => {
  router.push('/executions')
}

const handleFlowClick = (flowId: string) => {
  router.push(`/flows/${flowId}`)
}

// Format time ago helper
const formatTimeAgo = (dateString: string): string => {
  const date = new Date(dateString)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMins = Math.floor(diffMs / 60000)
  const diffHours = Math.floor(diffMs / 3600000)
  const diffDays = Math.floor(diffMs / 86400000)

  if (diffMins < 1) return 'Just now'
  if (diffMins < 60) return `${diffMins}m ago`
  if (diffHours < 24) return `${diffHours}h ago`
  return `${diffDays}d ago`
}

// Execution time display (for running items, show "Running")
const getExecutionTime = (execution: { status: string; started: string }): string => {
  if (execution.status === 'Running' || execution.status === 'Pending') {
    return 'Running'
  }
  return formatTimeAgo(execution.started)
}

// Status badge styling
const getStatusSeverity = (
  status: string,
): 'success' | 'danger' | 'info' | 'warn' | 'secondary' | undefined => {
  switch (status?.toLowerCase()) {
    case 'success':
      return 'success'
    case 'error':
      return 'danger'
    case 'running':
    case 'pending':
      return 'info'
    default:
      return 'secondary'
  }
}

// Format status for display
const formatStatus = (status: string): string => {
  if (!status) return 'Unknown'
  return status.charAt(0).toUpperCase() + status.slice(1).toLowerCase()
}

// Computed for formatted recent executions
const recentExecutions = computed(() => {
  return dashboardStore.recentExecutions.map((execution) => ({
    ...execution,
    time: getExecutionTime(execution),
    displayStatus: formatStatus(execution.status),
  }))
})

// Success rate change (mock for now, could be computed from historical data)
const successRateChange = computed(() => {
  // This would ideally come from comparing with previous period
  return dashboardStore.successRate > 90 ? '+1.2' : '-0.5'
})

// Flows this week change
const flowsChange = computed(() => {
  const total = dashboardStore.activeFlowsCount
  // Mock calculation - in real app would compare with previous week
  return total > 0 ? `${total} total` : 'No flows yet'
})

// Retry fetch on error
const handleRetry = () => {
  dashboardStore.clearError()
  dashboardStore.fetchDashboard()
}
</script>

<template>
  <div class="dashboard-view">
    <!-- Page Header -->
    <div class="flex align-items-center justify-content-between mb-5">
      <div>
        <h1 class="text-3xl font-bold m-0">Dashboard</h1>
        <p class="text-color-secondary mt-1 mb-0">Welcome back! Here's what's happening.</p>
      </div>
      <Button label="Create Flow" icon="pi pi-plus" @click="handleCreateFlow" />
    </div>

    <!-- Error State -->
    <Message v-if="dashboardStore.error" severity="error" :closable="false" class="mb-4">
      <div class="flex align-items-center justify-content-between w-full">
        <span>{{ dashboardStore.error }}</span>
        <Button label="Retry" icon="pi pi-refresh" size="small" text @click="handleRetry" />
      </div>
    </Message>

    <!-- Loading State - Stats Cards -->
    <div v-if="dashboardStore.isLoading && !dashboardStore.hasData" class="grid mb-5">
      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full">
          <template #content>
            <div class="flex align-items-center gap-3">
              <Skeleton shape="circle" size="48px" />
              <div class="flex-1">
                <Skeleton width="60%" height="14px" class="mb-2" />
                <Skeleton width="40%" height="24px" class="mb-2" />
                <Skeleton width="80%" height="12px" />
              </div>
            </div>
          </template>
        </Card>
      </div>
      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full">
          <template #content>
            <div class="flex align-items-center gap-3">
              <Skeleton shape="circle" size="48px" />
              <div class="flex-1">
                <Skeleton width="60%" height="14px" class="mb-2" />
                <Skeleton width="40%" height="24px" class="mb-2" />
                <Skeleton width="80%" height="12px" />
              </div>
            </div>
          </template>
        </Card>
      </div>
      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full">
          <template #content>
            <div class="flex align-items-center gap-3">
              <Skeleton shape="circle" size="48px" />
              <div class="flex-1">
                <Skeleton width="60%" height="14px" class="mb-2" />
                <Skeleton width="40%" height="24px" class="mb-2" />
                <Skeleton width="80%" height="12px" />
              </div>
            </div>
          </template>
        </Card>
      </div>
      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full">
          <template #content>
            <div class="flex align-items-center gap-3">
              <Skeleton shape="circle" size="48px" />
              <div class="flex-1">
                <Skeleton width="60%" height="14px" class="mb-2" />
                <Skeleton width="40%" height="24px" class="mb-2" />
                <Skeleton width="80%" height="12px" />
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>

    <!-- Stats Cards - Real Data -->
    <div v-else class="grid mb-5">
      <!-- Active Flows -->
      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full stat-card">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div
                class="flex align-items-center justify-content-center border-round"
                style="
                  width: 48px;
                  height: 48px;
                  background-color: var(--p-primary-100);
                  color: var(--p-primary-600);
                "
              >
                <i class="pi pi-bolt text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Active Flows</div>
                <div class="text-2xl font-bold">{{ dashboardStore.activeFlowsCount }}</div>
                <div class="text-color-secondary text-xs">{{ flowsChange }}</div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <!-- Executions Today -->
      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full stat-card">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div
                class="flex align-items-center justify-content-center border-round"
                style="
                  width: 48px;
                  height: 48px;
                  background-color: var(--p-green-100);
                  color: var(--p-green-600);
                "
              >
                <i class="pi pi-play text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Executions</div>
                <div class="text-2xl font-bold">{{ dashboardStore.executionsLast24h }}</div>
                <div class="text-color-secondary text-xs">Today</div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <!-- Success Rate -->
      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full stat-card">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div
                class="flex align-items-center justify-content-center border-round"
                style="
                  width: 48px;
                  height: 48px;
                  background-color: var(--p-teal-100);
                  color: var(--p-teal-600);
                "
              >
                <i class="pi pi-check-circle text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Success Rate</div>
                <div class="text-2xl font-bold">{{ dashboardStore.successRate.toFixed(1) }}%</div>
                <div class="text-color-secondary text-xs">
                  <span :class="successRateChange.startsWith('+') ? 'text-green-500' : 'text-red-500'">
                    {{ successRateChange }}%
                  </span>
                  from last week
                </div>
              </div>
            </div>
          </template>
        </Card>
      </div>

      <!-- Runners -->
      <div class="col-12 md:col-6 lg:col-3">
        <Card class="h-full stat-card">
          <template #content>
            <div class="flex align-items-center gap-3">
              <div
                class="flex align-items-center justify-content-center border-round"
                style="
                  width: 48px;
                  height: 48px;
                  background-color: var(--p-blue-100);
                  color: var(--p-blue-600);
                "
              >
                <i class="pi pi-desktop text-xl"></i>
              </div>
              <div>
                <div class="text-color-secondary text-sm">Runners</div>
                <div class="text-2xl font-bold">
                  {{ dashboardStore.runnersOnline }} / {{ dashboardStore.runnersTotal }}
                </div>
                <div class="text-color-secondary text-xs">Online</div>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>

    <!-- Main Content Grid -->
    <div class="grid">
      <!-- Recent Executions -->
      <div class="col-12 lg:col-8">
        <!-- Loading state for executions -->
        <Card v-if="dashboardStore.isLoading && !dashboardStore.hasData">
          <template #title>
            <div class="flex align-items-center justify-content-between">
              <span>Recent Executions</span>
              <Button label="View All" link size="small" disabled />
            </div>
          </template>
          <template #content>
            <div class="flex flex-column gap-3">
              <Skeleton height="40px" />
              <Skeleton height="40px" />
              <Skeleton height="40px" />
              <Skeleton height="40px" />
              <Skeleton height="40px" />
            </div>
          </template>
        </Card>

        <!-- Empty state for executions -->
        <Card v-else-if="recentExecutions.length === 0">
          <template #title>
            <div class="flex align-items-center justify-content-between">
              <span>Recent Executions</span>
              <Button label="View All" link size="small" @click="handleViewAllExecutions" />
            </div>
          </template>
          <template #content>
            <div class="flex flex-column align-items-center justify-content-center py-5">
              <i class="pi pi-inbox text-color-secondary" style="font-size: 3rem"></i>
              <p class="text-color-secondary mt-3 mb-2">No recent executions</p>
              <p class="text-color-secondary text-sm m-0">
                Run a flow to see execution history here
              </p>
              <Button
                label="Create Your First Flow"
                icon="pi pi-plus"
                class="mt-4"
                @click="handleCreateFlow"
              />
            </div>
          </template>
        </Card>

        <!-- Real data for executions -->
        <Card v-else>
          <template #title>
            <div class="flex align-items-center justify-content-between">
              <span>Recent Executions</span>
              <Button label="View All" link size="small" @click="handleViewAllExecutions" />
            </div>
          </template>
          <template #content>
            <DataTable :value="recentExecutions" :rows="5" class="p-datatable-sm">
              <Column field="flowId" header="Flow">
                <template #body="{ data }">
                  <span class="font-medium">{{ data.application || 'Flow' }}</span>
                </template>
              </Column>
              <Column field="displayStatus" header="Status">
                <template #body="{ data }">
                  <Tag :value="data.displayStatus" :severity="getStatusSeverity(data.status)" />
                </template>
              </Column>
              <Column field="time" header="Time"></Column>
            </DataTable>
          </template>
        </Card>

        <!-- Execution Trend Chart -->
        <Card class="mt-4">
          <template #title>Execution Trend (Last 7 Days)</template>
          <template #content>
            <div v-if="dashboardStore.isLoading && !dashboardStore.hasData">
              <Skeleton height="200px" />
            </div>
            <ExecutionTrendChart v-else :data="dashboardStore.executionTrends" />
          </template>
        </Card>
      </div>

      <!-- Quick Actions & Recent Flows -->
      <div class="col-12 lg:col-4">
        <Card class="mb-4">
          <template #title>Quick Actions</template>
          <template #content>
            <div class="flex flex-column gap-3">
              <Button
                label="Generate Flow with AI"
                icon="pi pi-sparkles"
                class="w-full"
                @click="handleCreateFlow"
              />
              <Button
                label="Create App"
                icon="pi pi-globe"
                severity="secondary"
                outlined
                class="w-full"
                @click="handleCreateApp"
              />
              <Button
                label="New Step"
                icon="pi pi-puzzle-piece"
                severity="secondary"
                outlined
                class="w-full"
                @click="handleNewStep"
              />
            </div>
          </template>
        </Card>

        <Card>
          <template #title>Top Flows</template>
          <template #content>
            <!-- Loading state -->
            <div v-if="dashboardStore.isLoading && !dashboardStore.hasData" class="flex flex-column gap-3">
              <Skeleton height="48px" />
              <Skeleton height="48px" />
              <Skeleton height="48px" />
            </div>

            <!-- Empty state -->
            <div
              v-else-if="dashboardStore.topFlows.length === 0"
              class="flex flex-column align-items-center py-4"
            >
              <i class="pi pi-folder-open text-color-secondary" style="font-size: 2rem"></i>
              <p class="text-color-secondary mt-2 mb-0 text-sm">No flows yet</p>
            </div>

            <!-- Real data -->
            <div v-else class="flex flex-column gap-3">
              <div
                v-for="flow in dashboardStore.topFlows"
                :key="flow.flowId"
                class="flex align-items-center justify-content-between p-2 border-round hover:surface-hover cursor-pointer"
                @click="handleFlowClick(flow.flowId)"
              >
                <div class="flex align-items-center gap-2">
                  <i class="pi pi-bolt text-color-secondary"></i>
                  <span>{{ flow.flowName }}</span>
                </div>
                <div class="flex align-items-center gap-2">
                  <Tag
                    :value="`${flow.successRate.toFixed(0)}%`"
                    :severity="flow.successRate >= 90 ? 'success' : flow.successRate >= 70 ? 'warn' : 'danger'"
                    class="text-xs"
                  />
                  <span class="text-color-secondary text-sm">{{ formatTimeAgo(flow.lastExecuted) }}</span>
                </div>
              </div>
            </div>
          </template>
        </Card>
      </div>
    </div>
  </div>
</template>

<style scoped>
.dashboard-view {
  max-width: 1400px;
  margin: 0 auto;
}

.stat-card {
  transition: transform 0.2s ease, box-shadow 0.2s ease;
}

.stat-card:hover {
  transform: translateY(-2px);
  box-shadow: var(--p-card-shadow);
}
</style>
